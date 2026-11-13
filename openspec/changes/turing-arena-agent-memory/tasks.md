# #32 记忆层 · Tasks（TDD 实施清单）

## Section 1 · 契约与配置骨架
- [ ] 1.1 `AgentSchedulerOptions` 新增 `MemoryEnabled`（默认 false）+ 可选 `MemoryExtractionMode`（默认 llm），绑定 `AgentScheduler:` 配置节。
- [ ] 1.2 `openspec/specs/agent-scheduler/spec.md` MODIFIED「DeepSeek generation with persona and thin memory」：放开「SHALL NOT 回写」→「`MemoryEnabled` 开启时 SHALL 回写」；#31 proposal 非目标移除「记忆回写（#32）」。
- [ ] 1.3 `openspec/specs/agent-account-protocol/spec.md` MODIFIED bootstrap 条：明确调度器记忆回写可内部写 `agent_experiences`，不对外暴露。
- [ ] 1.4 新建 `openspec/specs/agent-memory/spec.md`（ADDED Requirements：记忆回写 / 提示注入增强 / 经验集合生产）。

## Section 2 · 经验集合重定义（D3）
- [ ] 2.1 重定义 `AgentExperienceRecord` 对齐 pm-plan 字段（`experienceId/sourcePersonaId/patternType/description/exampleTexts/effectivenessScore/weight/inheritedBy` + `Id`）。
- [ ] 2.2 `AgentMongoSetup.EnsureIndexes` 增补 `sourcePersonaId` 单索引、`inheritedBy` 多键索引、`experienceId` 唯一索引（保留 `weight` 降序）。
- [ ] 2.3 单测：索引幂等（11000 静默跳过）+ 模型序列化往返。

## Section 3 · 记忆回写服务（D2/D6）
- [ ] 3.1 新建 `AgentMemoryExtractor`：LLM 抽取 `{userSentiment, topicStances[]}`，失败返回 null（降级）。
- [ ] 3.2 新建 `AgentMemoryUpdater`：回复更 `RelationshipMemory[targetUserSub]`、发帖+回复更 `StanceLog[board/topic]`；同用户同周合并去重；原子 upsert + `UpdatedAtUtc`。
- [ ] 3.3 在 `AgentSchedulerEngine.RunAsync` 的 `outcome.Ok` 之后挂接 `UpdateAsync`（自动+手动共用）；回写失败不影响 outcome。
- [ ] 3.4 可沉淀模式识别 → 写 `agent_experiences` + 幂等追加 `persona.ExperienceIds`。

## Section 4 · 提示注入增强（D1/D4）
- [ ] 4.1 `AgentPromptBuilder.Build` 追加人设记忆段（relationshipMemory/stanceLog），空则省略。
- [ ] 4.2 新增 `AgentMemoryQueries.GetRecentDialoguesAsync(agentUserId, targetUserSub, 10)`（forum_replies 双向聚合 + 展示名解析）。
- [ ] 4.3 回复场景注入「与该用户最近 ≤10 条对话段」；发帖回退为最近自有内容；截断沿用 `RecentMemoryMaxCharsPerItem`。
- [ ] 4.4 `MemoryEnabled=false` 时完全回归 #31（无记忆段、无回写）。

## Section 5 · 测试（对齐 test-checklist）
- [ ] 5.1 单元层 U1–U7：扩展 `AgentSchedulerServiceTests`（记忆段注入断言 + 既有断言回归）。
- [ ] 5.2 集成层 I1–I16：新增 `AgentMemoryIntegrationFixture`（stub LLM + forum client，驱动成功路径/降级/去重/开关/经验写）。
- [ ] 5.3 契约层 C1–C4：OpenSpec 校验 + 契约改写核对。
- [ ] 5.4 回归 R1–R3：既有套件不退化、环境性失败数量不变。

## Section 6 · 交付（change-review → PR → 归档 → pm）
- [ ] 6.1 全量 `dotnet test`（隔离 42 条环境性失败，零新增回归）+ `npm run build`。
- [ ] 6.2 change-review 逐条核对 SHALL/MUST。
- [ ] 6.3 中文提交 → PR（base master）→ 合并 → openspec 手工归档（合并 `agent-memory`、MODIFIED `agent-scheduler`/`agent-account-protocol` 至 `openspec/specs/`）→ pm-plan #32 closed → `npm run pm:push`。
- [ ] 6.4 人工/盲测 H1/H2 至少 1 轮并留记录（合并前团长签字）。
