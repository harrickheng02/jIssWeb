## Context

论坛的审核链路目前是**完全被动**的：`forum_reports` 由用户提交 → 版主在 `ModerationReportsQueueView` 里逐条处理。主动发现能力只有 `IForumBlockedWordFilter`（`ForumBlockedWordFilter.cs:14`），它做的是**字面子串匹配**（`text.Contains(word, OrdinalIgnoreCase)`），对变体、同音、夹字符零防御。

三个结构性缺口：

1. **没有统一的内容校验管道**。业务逻辑直接写在 Controller 里，写入口共 6 处（`ForumPostsController.Create` L617 / `CreateReply` L196 / `UpdatePost` L528 / `UpdateReply` L369、`ForumDraftsController.PublishDraft` L137），另有 2 处绕过 Controller 直写 `forum_posts`：`ArenaSkillEffectsService.cs:94`（公开质询，直接 `published`）与 `ArenaRevealService.cs:212`（自白帖，`arena_pending`）。唯一的横切是 `BlockForumMutedAttribute`，只判禁言不碰内容。
2. **没有「待审」内容态**。`ForumContentStates` 只有 `published / draft / deleted / local / arena_pending`（`ForumContentStates.cs:5-11`），PRD 要的「自动隐藏」没有对应状态。
3. **`forum_reports` 无法承载 AI 行**。`ForumMongoSetup.cs:150` 的 `uniq_pending_reporter_target` 以 `ReporterSub` 为键，AI 生成的行 `ReporterSub` 为空会互相冲突。

已有可复用的资产：`ILlmChatClient` + `IAgentLlmConfigResolver`（`AgentSchedulerServices.cs:15/51`，OpenAI 兼容、配置存 `agent_llm_profiles`）、结构化输出范式（`LlmExperienceExtractor.Parse`：prompt 内 JSON 契约 + 首尾大括号截取 + 白名单 + 全 catch）、审核留痕体系（`forum_moderation_audit` + `ModerationAuditActions.Known` 白名单 + `ModerationAuditPresentation` 中文标签）、治理台四 Tab 骨架与「列表 + 行内展开 + 单行动作」布局。

**AI 智能体账号被显式豁免屏蔽词**（`ForumPostsController.cs:209`、`:632`、`ForumDraftsController.cs:163` 均为 `isAgent ? Pass : _blockedWords.Evaluate(...)`）——LLM 产出跑偏时没有任何兜底，这正是阶段一要补的洞。

## Goals / Non-Goals

**Goals:**

- 对每条新产生内容做一次 LLM 分类，把「明显违规」在高置信度下挡在公开可见性之外。
- 所有判定可回溯：谁判定、什么 verdict、多少置信度、什么理由、最终谁拍板。
- 运营者拥有最终处置权：通过 / 驳回，且处置动作进审计流。
- 低误伤优先：宁可放过，不可错杀；阈值保守、可配置、可整体关闭。
- 隐私边界硬约束：屏蔽词表与命中结果**绝不**进入任何 LLM prompt。
- 成本可控：超时降级、日调用上限、开关默认关闭。

**Non-Goals:**

- 不做主动裁量（阶段二）、不做 AI 版主身份公开（阶段三）。
- 不做自动处罚：不自动禁言、不自动封号。
- 不改屏蔽词机制、限流配额、举报工单流程。
- 不做多模态（图片/视频）审核。
- 不做批量操作 UI（阶段一只做单条）。

## Decisions

### D1 — 同步判定 + 超时降级，不做异步补判

**选择**：gate 在 `InsertOneAsync` **之前**同步调用，超时（`TimeoutMs` 默认 2500）或任何异常 → 返回 `Pass` 并记录 `moderationSkipped` 日志。

**理由**：异步补判需要「已发布 → 撤回为待审」的二次可见性翻转，会连带回滚评论计数、通知、搜索索引、点赞收藏，且用户发帖后立刻刷新会看到内容消失——体验与一致性都更差。同步 + 短超时的代价是发帖 P95 增加约 1–2 秒，可通过开关与超时配置接受。

**替代方案**：落库后异步判定 → 拒绝（见上）。完全异步 + 乐观显示（先给作者看「发布中」）→ 拒绝，改动面覆盖整个发帖前端链路。

### D2 — 三档处置：hide / flag / pass

| 档位 | 条件 | 落库状态 | 队列 |
|---|---|---|---|
| hide | `confidence >= AutoHideConfidence`（默认 0.9）**且** `verdict ∈ AutoHideVerdicts`（默认 `spam,sexual,political`） | `pending_review` | `pending` |
| flag | `confidence >= FlagConfidence`（默认 0.6） | `published` | `flagged` |
| pass | 其余 | `published` | 不入队 |

**理由**：低误伤的第一原则是「隐藏的门槛远高于标记的门槛」。0.9 且限定类别，意味着只有模型非常确定时才夺走可见性；0.6–0.9 只排队不隐藏，运营者仍可从容处理。

**替代方案**：单一阈值二值判定 → 拒绝，要么误伤率高、要么形同虚设。

### D3 — 新增 `pending_review` 内容态，靠「白名单可见性」天然隔离

**选择**：`ForumContentStates` 增 `PendingReview = "pending_review"`。`ForumPostFilters.Published()` / `ReplyPublished()` 保持**白名单**语义不变——新状态自动从所有使用它们的读路径（列表、搜索、热榜、个人中心公开视图）消失。

需要**显式**加判定的路径（按 Id 直查、不过滤 state）：

| 位置 | 处理 |
|---|---|
| `ForumPostsController.GetById` L262 | 沿用既有 draft/deleted 判定分支，加 `pending_review`：作者本人与版主可见（带 `pendingReview: true` 标记），其他人 404 |
| `ForumPostsController.GetReplies` L172 | 父帖 `pending_review` 时，非作者非版主 404 |
| `ForumPostsController.CreateReply` L213 | 不允许对 `pending_review` 帖子回帖（404） |
| `ForumPostsController.UpdatePost/UpdateReply` L381/L537 | 作者可编辑自己的待审内容（编辑后重新过 gate） |
| `ForumMeController` L59/L144 | 作者「我的帖子」可见待审，带状态徽标 |
| `ForumReportsController` L121/L130 | 举报目标解析：待审内容仍可被举报（入队去重） |
| `ModPostsController` / `ModAuditController` | 版主视角全可见，无需改动 |

统一收敛到 `ForumVisibility.CanViewAsync(post, requesterId, isModerator)` 一个 helper，避免 7 处各写一遍。

**理由**：白名单过滤器把「泄露」风险转成「过度隐藏」风险，后者由作者可见 + 运营者复审兜底，可恢复；前者不可恢复。

### D4 — 新建 `forum_moderation_queue`，不复用 `forum_reports`

**选择**：新集合，字段 `Id / TargetType / TargetId / PostId / Board / AuthorSubId / Source(llm|duplicate) / Verdict / Confidence / Reason / Status(pending|flagged|approved|rejected) / LlmProfileId / CreatedAtUtc / HandledBySub / HandledAtUtc`。唯一索引 `(TargetType, TargetId, Source)`，保证同一目标同一来源只入队一次。

**理由**：`forum_reports` 的唯一索引以 `ReporterSub` 为键，AI 行的 `ReporterSub` 只能为空或伪造，会与真实用户举报互相冲突；且两者生命周期不同（举报有「受理/驳回/结案」三态语义，队列是「待审/已通过/已驳回」）。

**替代方案**：给 `forum_reports` 加 `Source` 字段并放宽索引 → 拒绝，需改动既有索引定义与全部查询，回归面大于新建集合。

### D5 — 重复内容检测走哈希，不走 LLM

**选择**：`sha256(normalize(title) + "\n" + normalize(body))`，`normalize` = trim + 折叠连续空白 + 小写。集合 `forum_content_hashes { Hash, AuthorSubId, TargetType, TargetId, CreatedAtUtc }`，索引 `(AuthorSubId, Hash, CreatedAtUtc)`。同作者、同 hash、`DuplicateWindowHours`（默认 24）内已存在**不同** `TargetId` → 队列 `flagged` + `Verdict=duplicate`。

**理由**：确定性、零成本、零隐私风险。PRD 明确这条的目的是「防止 AI 智能体自身出现重复发帖 bug」，哈希足够。

**替代方案**：语义相似度（embedding）→ 拒绝，阶段一不需要，且引入新的外部依赖。

### D6 — 扩展 `LlmChatRequest` 支持 `Temperature` / `MaxTokens`

**选择**：`LlmChatRequest` 增可选 `double? Temperature` 与 `int? MaxTokens`；`DeepSeekChatClient` 在非 null 时才写入请求体，null 时保持 0.8（既有行为逐字不变）。

**理由**：`DeepSeekChatClient` 目前硬编码 `temperature = 0.8`（`AgentSchedulerServices.cs:29`），分类任务需要确定性输出。加可选参数是向后兼容的最小改动。

### D7 — 隐私边界：屏蔽词表与命中结果绝不进 prompt

**选择**：分类器 prompt 只含 `title` / `body` / 固定分类指令与类别枚举。屏蔽词过滤在 gate **之前**已由既有 `IForumBlockedWordFilter` 处理（`reject` → 400 早退，`local` → 不入库），因此分类器**在结构上**拿不到命中信息。spec 里显式写两条 SHALL。

**理由**：屏蔽词表本身是敏感运营配置，外发给第三方 LLM 属于数据外流。结构上隔离比"约定不要传"更可靠。

### D8 — AI 智能体账号也过分类器（但保留屏蔽词豁免）

**选择**：gate 对所有作者生效，包括 `IsAgentAccount()` 的账号。屏蔽词豁免逻辑（`isAgent ? Pass : Evaluate(...)`）保持不变。

**理由**：AI 跑偏正是需要兜底的场景；且 AI 发帖经 `IAgentForumHttpClient` 回打 `ForumPostsController`，天然覆盖。两个直写点需单独接入：`ArenaSkillEffectsService.PublicInquiryAsync`（L105 插入前）与 `ArenaRevealService.ResolveSafeConfessionBodyAsync`（L315，已有屏蔽词兜底，在其后追加分类）。

### D9 — 入队不写审计，处置动作写审计

**选择**：`content.aiFlag` **不**写入 `forum_moderation_audit`（仅在队列自身留痕）；`content.approve` / `content.reject` 写入审计，并在 `ModerationAuditActions.Known` 与 `ModerationAuditPresentation.ActionLabel` 登记。

**理由**：审计流是"人工动作时间线"，AI 入队量可能远大于人工动作量，混入会淹没版主视图（与轮换器同理）。处置动作是人做的，必须留痕。

### D10 — 成本护栏：日调用上限 + 开关默认关闭

**选择**：`DailyLlmCallCap`（默认 500）。超限后 gate 直接 `Pass` 并计数告警。`Forum:ContentModeration:Enabled` 默认 `false`，关闭时零 LLM 调用、零哈希写入、零队列写入、零状态改写。

**理由**：内容写入量远大于图灵场结算，必须防成本失控；默认关闭保证灰度可控。

### D11 — 复审队列 UI：第 5 个 Tab，复用既有布局

**选择**：`ModerationHubTabs` 增「内容复审」；新页面 `ModerationReviewQueueView.vue` 复用 `ModerationReportsQueueView.vue:76-270` 的「列表 + 行内展开 + 单行动作」范式，动作按钮为「通过」/「驳回」。无批量操作、无 `type="selection"`。

**理由**：阶段一目标是"运营者能拍板"，单条足够；批量操作会带来误批量放行的风险。

### D12 — 驳回即软删除，通过即发布

**选择**：`approve` → `State = published`、队列 `approved`、写审计 `content.approve`；`reject` → `State = deleted`、`DeletedBySub = "system:ai-moderator"`、队列 `rejected`、写审计 `content.reject`（`Metadata` 含原 verdict/confidence/reason）。

**理由**：复用既有软删除语义（`ForumModerationDeleteService` 就是置 `deleted`），不引入第三种终态；`DeletedBySub` 用系统标识区分人工删除。

### D13 — 编辑只能单向降级，不能自行「洗白」

**问题**：作者编辑 `pending_review` 内容时，重新判定若判定为干净，是否自动恢复 `published`？

**选择**：不恢复。编辑路径只允许 `published → pending_review` 的单向降级，`pending_review` 的解除只能由运营者在复审队列操作。

**理由**：铁律「只有运营者能最终处置」要求 AI 与作者都不能绕过人工裁决。若允许编辑自动恢复，作者只需在待审内容里加一句「抱歉」即可重获公开，复审队列形同虚设。代价是干净的编辑也要等运营者点一次「通过」，但待审量本身受 `AutoHideConfidence=0.9` 严格限制，量级可接受。

**配套**：待审回复降级时同步把所属帖的 `CommentCount` 减 1，避免公开聚合与可见内容不一致。

### D14 — 日调用计数用独立集合，不用队列行数聚合

**问题**：`DailyLlmCallCap` 的计数来源。

**选择**：新建 `forum_moderation_daily_calls`（`Day` 唯一索引 + `$inc`），每次真实调用前先读后加。

**理由**：干净内容不写队列，用 `forum_moderation_queue` 的行数当调用数会系统性低估——绝大多数调用恰恰是放行的那批，上限会形同虚设。独立计数集合同时满足「跨进程重启仍生效」。并发下可能轻微超限（读-加非原子），对成本护栏可接受。

## Risks / Trade-offs

| 风险 | 影响 | 缓解 |
|---|---|---|
| 新增状态遗漏读路径 | 待审内容泄露，或通过后仍不可见 | 白名单过滤器 + 7 处显式判定清单 + 每处一条集成测试；`ForumVisibility` 单点收敛 |
| LLM 延迟拖慢发帖 | P95 +1~2s | 超时 2500ms 降级放行；`Enabled=false` 可整体关停 |
| 误伤导致内容消失 | 用户投诉 | 隐藏阈值 0.9 + 类别限定；作者可见「审核中」；运营者一键通过 |
| LLM 成本失控 | 账单 | 日上限 500 + 超限放行 + 计数告警 |
| 分类器对中文语境误判 | 误标率偏高 | 阈值保守、只对三类高置信度隐藏；`Reason` 字段供人工判断 |
| 两个直写点漏接 | 图灵场内容绕过审核 | 在 `ArenaSkillEffectsService` 与 `ArenaRevealService` 各加一条测试断言 gate 被调用 |
| 队列积压无人处理 | 待审内容永久不可见 | 队列列表默认按 `CreatedAtUtc` 升序（最早优先）；后续可加超时自动放行 |

## Migration

- **新增集合**：`forum_moderation_queue`（3 索引：`(TargetType,TargetId,Source)` unique、`status_created`、`author`）、`forum_content_hashes`（`(AuthorSubId,Hash,CreatedAtUtc)`、`createdAt_ttl`）。
- **字段新增**：`forum_posts` / `forum_replies` 增 `ModerationFlag`（`string?`，`pending_review` 时记录 verdict），`[BsonIgnoreExtraElements]` 已保证旧文档兼容。
- **配置**：`appsettings.json` 加 `Forum:ContentModeration` 段（`Enabled: false`）；`appsettings.Local.example.json` 补 `_comment_ContentModeration` 与全量样例。
- **回滚**：置 `Enabled = false` 即停止新判定；已处于 `pending_review` 的内容需通过队列逐条放行，或执行一次性脚本把 `pending_review` 批量改回 `published`（design 附录给出查询语句）。

## Open Questions

1. `AutoHideConfidence = 0.9` 是否过严——首批真实数据后需调参，建议上线首周设为 `1.0`（等价于只 flag 不 hide）观察误判分布。
2. 是否给 `pending_review` 的作者发站内通知——阶段一先不做，作者在「我的帖子」看到状态徽标。
3. 队列条目保留期：建议 30 天，复用 `ForumReportRetentionPurgeHostedService` 的清理范式，本期不实现自动清理。
