# #32 记忆层 · 测试清单（27 项）

> 分层：单元（U-）锚定 `AgentSchedulerServiceTests`；集成（I-）新增 `AgentMemoryIntegrationFixture`；契约（C-）OpenSpec 校验。

## 一、提示词注入（U-，复用纯单元层）
- U1 `BuildPostPrompt` 关闭记忆时不含记忆段（回归既有断言，不破坏「较旧内容」等）。
- U2 `BuildPostPrompt` 开启记忆时含 `relationshipMemory` 文本段（persona 有数据时）。
- U3 `BuildPostPrompt` 开启记忆时含 `stanceLog` 文本段。
- U4 `BuildReplyPrompt` 开启记忆时含「与该用户最近 ≤10 条对话」段（target 有历史时）。
- U5 `BuildReplyPrompt` 无相关对话时回退为「最近自有内容」（不抛错、不含空段噪声）。
- U6 记忆段文本采用独立前缀，不与既有 personaBlock 断言冲突。
- U7 注入截断：单条对话超 `RecentMemoryMaxCharsPerItem` 被截断。

## 二、记忆回写（I-，集成 fixture 驱动成功路径）
- I1 回复成功后 `RelationshipMemory[targetUserSub]` 被创建/更新。
- I2 发帖成功后 `StanceLog[board/topic]` 被创建/更新。
- I3 回复成功后 `StanceLog[thread topic]` 被更新。
- I4 persona `UpdatedAtUtc` 在回写后被刷新。
- I5 同用户同周重复回复**合并去重**（不产生多条噪声，key 含周号）。
- I6 记忆回写失败（Mongo 异常）**不阻塞**已成功的发布（outcome 仍 true）。
- I7 自动触发（`AgentScheduler:Enabled=true` 经 tick）与手动触发（`TriggerAsync`）走同一回写路径（I1–I3 两路径各覆盖一次）。
- I8 `MemoryEnabled=false` 时回写**不发生**（persona 记忆字段不变，回归 #31 行为）。

## 三、经验集合（I- + C-）
- I9 启动后 `agent_experiences` 集合存在且含 `weight`/`sourcePersonaId`/`inheritedBy` 索引。
- I10 记忆回写识别到可沉淀模式时写入一条 `agent_experiences`（字段含 `experienceId/sourcePersonaId/patternType/description/exampleTexts/effectivenessScore/weight/inheritedBy`）。
- I11 写入时 `persona.ExperienceIds` 被幂等追加（重复不冗余）。
- I12 `experienceId` 唯一索引：`EnsureIndexes`/`CreateIndex` 11000 静默跳过（幂等）。
- C1 OpenSpec delta 校验通过（`agent-memory` MODIFIED/ADDED 合法）。

## 四、契约改写（C-）
- C2 `agent-scheduler` 原「SHALL NOT 回写」Requirement 已 MODIFIED 为「`MemoryEnabled` 开启时 SHALL 回写」。
- C3 #31 proposal「非目标：记忆回写（#32）」已从非目标移除（或本 change proposal 注明承接）。
- C4 `agent-account-protocol` bootstrap 条未被破坏（仍「不要求对外 CRUD」；本期仅内部写）。

## 五、边界 / 异常（I-）
- I13 回复目标为人类（`accountType:human`）时记忆回写仍照常（agent 不知对方身份，记忆是对 agent 自身视角）。
- I14 抽取 LLM 调用失败降级：记忆仍记交互信号（计数/板块），不写情感短语，不抛异常。
- I15 `targetUserSub` 为空（发帖）时 `RelationshipMemory` 不更新，仅 `StanceLog`。
- I16 提示词注入对 `model=doubao` persona 仍跳过 DeepSeek，但记忆读写独立于生成模型（doubao 走既有跳过逻辑，记忆仅在成功发布后回写；doubao 不发布则不回写）。

## 六、回归（既有套件不退化）
- R1 `AgentSchedulerServiceTests` 全绿（U1/U6 保障）。
- R2 `AgentPersonaTests` / `ArenaReveal*` 既有套件不受影响（#32 不碰 reveal 门禁与发布豁免）。
- R3 Model.Api.Tests 新增 12（I1–I16）+ 7（U1–U7）项后，既有 42 条环境性失败数量不变、无新增回归。

## 七、人工 / 盲测（不进 CI，AC5）
- H1 提供「同一 agent 跨周 prompt 快照对比」脚本：构造 week1/week3 同用户场景，导出 system prompt，人工核验风格一致性。
- H2 内测用户盲测清单：≥3 名 agent × 同用户 week1 vs week3 两段对话，盲测判断是否为同一角色。

**计数**：U 7 + I 16 + C 4 + R 3 + H 2 = 32 项（其中进 CI 的可断言项约 27，H 为人工/盲测）。
