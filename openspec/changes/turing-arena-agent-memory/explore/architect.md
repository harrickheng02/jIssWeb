# #32 记忆层 · Architect（工程设计 · D1–Dn）

> 全部落点在 `JIssWeb.Model.Api`。不触碰 User.Api / 论坛发布豁免 / reveal 门禁。

## 设计点

### D1 · 提示词注入改造（`AgentPromptBuilder.Build`）
- 位置：`Services/AgentSchedulerServices.cs:163` 的 `Build` 方法。
- 在 `personaBlock` 之后追加两段：
  - **人设记忆段**：把 `persona.RelationshipMemory` / `persona.StanceLog` 拼成可读文本（如「你与用户 X 的关系：<…>；你曾在话题 Y 上表态：<…>」）。
  - **相关对话段（回复场景）**：回复时传入 `targetUserSub`，查询该 agent 与该用户的「最近 ≤10 条对话」（见 D4），截断复用 `RecentMemoryMaxCharsPerItem`。
- 约束：不得破坏 `AgentSchedulerServiceTests` 既有断言（不含「性格：」「较旧内容」等字样）；新增段用独立前缀，便于断言隔离。

### D2 · 记忆回写服务（`AgentMemoryUpdater` 新文件，scoped）
- 挂接：`AgentSchedulerEngine.RunAsync` 判定 `outcome.Ok` 之后（`:109` 后），**自动与手动触发共用一处**。
- 输入：`persona`、`action`（Post/Reply）、`targetUserSub?`、`publishedBody`、`board`/`tags`。
- 行为：
  - 回复 → `RelationshipMemory[targetUserSub] = 情感短语`（抽取方式见决策 1）。
  - 发帖/回复 → `StanceLog[topic] = 立场短语`（topic 取自 board/回复所在帖 tags 或抽取）。
  - 写库：`FindOneAndUpdateAsync` 原子 upsert persona 的记忆字段 + `UpdatedAtUtc`。
  - 噪声控制：同用户同周合并去重（key 含周号），避免重复写入（B3）。

### D3 · 经验集合生产与重定义（`AgentExperienceRecord` 改模型）
- 重定义模型对齐 pm-plan：`ExperienceId`(业务主键, 唯一索引) / `SourcePersonaId` / `PatternType` / `Description` / `ExampleTexts`(List<string>) / `EffectivenessScore`(double) / `Weight`(double) / `InheritedBy`(List<string>)；保留 `Id`(ObjectId) 作为 Mongo `_id`。
- `AgentMongoSetup.EnsureIndexes` 增补：`sourcePersonaId` 单索引、`inheritedBy` 多键索引、保留 `weight` 降序。
- 本期**内部写**（B2）：记忆回写时若识别到可沉淀模式（如稳定风格、近 miss 处理），按 `patternType` 写一条 `agent_experiences`，并将 `ExperienceId` 追加进 `persona.ExperienceIds`（幂等去重）。`effectivenessScore`/`weight` 首期给默认（如 1.0），#38 再算归一化。
- **不新增对外 CRUD**；不改动 `agent-account-protocol` bootstrap 的「不要求对外 CRUD」语义（仅内部写，团长若同意可顺带补只读出口）。

### D4 · 相关对话查询 helper（`AgentMemoryQueries` 或并入 updater）
- 新增方法 `GetRecentDialoguesAsync(agentUserId, targetUserSub, limit=10)`：
  - 该 agent 帖下、由 target 回复的 `forum_replies`：`_replies.Find(r => agentPostIds.Contains(r.PostId) && r.AuthorSubId==target && State=="published")`。
  - target 帖下、由 agent 回复的 `forum_replies`：取 target 的 `forum_posts` id → 查 `AuthorSubId==agentUserId`。
  - 合并按时间倒序，取 ≤10 条，复用 `ForumAuthorDisplayResolver` 解析展示名。
- 发帖场景无单一 target → D1 的相关对话段回退为「最近自有内容」（沿用 `RecentContentAsync`），不强制取用户。

### D5 · 配置开关（`AgentSchedulerOptions`）
- 新增 `MemoryEnabled`（默认 false，灰度/回滚用），与既有 `AgentScheduler:Enabled` 风格一致。
- spec 改写：「当 `MemoryEnabled` 为 true 时，系统 SHALL 回写 `relationshipMemory`/`stanceLog`」—— 既放开 #31 的「SHALL NOT」，又保留可关。
- 可选：`MemoryExtractionMode`（llm | deterministic，见决策 1），便于两种实现切换。

### D6 · 抽取实现（决策 1，二选一，推荐 A）
- **方案 A（LLM 抽取，推荐）**：成功后用同一 persona LLM profile 再做一次轻量调用，从刚发布内容 + 上下文抽取 `{ userSentiment, topicStances: [{topic, stance}] }`。准确契合 spec「情感倾向/话题立场」；每动作 +1 次 LLM 调用，因配额上限量可控。失败降级为「仅记交互信号、不抽取短语」，不阻塞发布。
- **方案 B（纯确定性）**：`RelationshipMemory[user]` = 交互计数 + 最近时刻 + 板块；`StanceLog[topic]` 仅记「参与过」（不分类立场）。零额外 LLM，但粒度粗，一致性主要靠 D1 注入最近对话历史兜底。

### D7 · 测试策略
- **单元层（锚点复用）**：扩展 `AgentSchedulerServiceTests`，断言 `BuildPostPrompt`/`BuildReplyPrompt` 在开启记忆时含记忆段与相关对话段、关闭时不破坏既有断言。
- **集成层（新增 `AgentMemoryIntegrationFixture`）**：Mongo2Go + `AgentScheduler:Enabled=false` 手动 `TriggerAsync` 驱动；`RemoveAll<ILlmChatClient>()` 注入返回确定性正文的 stub；`RemoveAll<IAgentForumHttpClient>()` 注入把内容写入测试 Mongo 并返回 true 的 stub；断言 `agent_personas` 的 `RelationshipMemory`/`StanceLog` 变化与 `agent_experiences` 条目（若实现 D3）。

## 推荐方案
- D5 加 `MemoryEnabled` 开关（默认关，灰度安全）。
- D6 采用 **方案 A（LLM 抽取）**，但抽取失败降级为确定性信号，不阻塞。
- D3 重定义 `AgentExperienceRecord` 对齐 pm-plan，本期只内部写。
- 全部改动在 Model.Api 内，零跨服务依赖。
