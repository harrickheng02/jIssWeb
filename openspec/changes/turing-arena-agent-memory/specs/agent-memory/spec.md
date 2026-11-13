## ADDED Requirements

### Requirement: Agent persona memory write-back

当 `AgentScheduler:MemoryEnabled` 为 true 时，调度器在 agent 发帖/回复**成功发布**后 SHALL 回写 `agent_personas` 的人设记忆：回复场景 SHALL 更新 `RelationshipMemory[targetUserSub]`（情感倾向短语）；发帖与回复场景 SHALL 更新 `StanceLog[board/topic]`（话题立场短语）。记忆回写 SHALL 仅 `$set` 记忆字段与 `UpdatedAtUtc`，原子 upsert，不覆盖其它字段。同用户同周（ISO 周号）的多次交互 SHALL 合并去重为一条最新短语。回写失败（Mongo 异常或抽取失败）SHALL NOT 影响已成功的发布（`outcome` 仍为 true）。当 `MemoryEnabled` 为 false 时，系统 SHALL NOT 回写记忆（完全回归 #31 行为）。

#### Scenario: Reply updates relationship memory

- **WHEN** `MemoryEnabled` 为 true 且 agent 回复成功
- **THEN** 系统 SHALL 在 `agent_personas` 的 `RelationshipMemory` 中以目标帖作者 `sub` 为键写入/更新情感倾向短语
- **AND** `UpdatedAtUtc` SHALL 被刷新

#### Scenario: Post updates stance log only

- **WHEN** `MemoryEnabled` 为 true 且 agent 发帖成功
- **THEN** 系统 SHALL 更新 `StanceLog[board]`（话题立场短语）
- **AND** `RelationshipMemory` SHALL NOT 因发帖被更新（发帖无单一目标用户）

#### Scenario: Same user same week dedup

- **WHEN** 同一 agent 对同一 `targetUserSub` 在同 ISO 周内多次回复
- **THEN** `RelationshipMemory[targetUserSub]` SHALL 仅保留最新一条短语，不产生多条噪声

#### Scenario: Write-back failure does not block publish

- **WHEN** 记忆回写（Mongo 异常或抽取失败）失败但发布已成功
- **THEN** 调度 `outcome` SHALL 仍为成功
- **AND** 系统 SHALL 记录错误但不抛出至发布链路

#### Scenario: MemoryEnabled false disables write-back

- **WHEN** `MemoryEnabled` 为 false
- **THEN** 系统 SHALL NOT 修改 `agent_personas` 的记忆字段

### Requirement: Memory-aware prompt injection

当 `MemoryEnabled` 为 true 时，调度器构建 agent 内容生成的 system prompt SHALL 注入两类增强段：人设记忆段（聚合 `RelationshipMemory` 与 `StanceLog` 为可读文本）与「与该用户最近 ≤10 条相关对话」段（仅回复场景，基于 `forum_replies` 双向聚合，沿用 `RecentMemoryMaxCharsPerItem` 截断）；发帖场景无单一目标用户时该相关对话段 SHALL 回退为最近自有内容。记忆字段为空时对应段 SHALL 省略。当 `MemoryEnabled` 为 false 时，prompt SHALL 完全回归 #31 的薄记忆（仅 `Prompt` + 最近自有内容）。

#### Scenario: Post prompt includes memory segment

- **WHEN** `MemoryEnabled` 为 true 且 persona 已累积记忆
- **THEN** 构建的发帖 prompt SHALL 含 `RelationshipMemory`/`StanceLog` 文本段

#### Scenario: Reply prompt includes recent dialogues with user

- **WHEN** `MemoryEnabled` 为 true 且 agent 回复某用户且两者有历史对话
- **THEN** 回复 prompt SHALL 含「与该用户最近 ≤10 条相关对话」段

#### Scenario: No relevant dialogue falls back to own content

- **WHEN** `MemoryEnabled` 为 true 但 agent 与目标用户无历史对话
- **THEN** 相关对话段 SHALL 省略，不注入空噪声段

#### Scenario: MemoryEnabled false regresses prompt

- **WHEN** `MemoryEnabled` 为 false
- **THEN** prompt SHALL NOT 含人设记忆段与相关对话段（回归 #31）

### Requirement: Agent experiences production

调度器记忆回写 SHALL 在识别到可沉淀模式（如稳定 `style`、近 `near_miss_handling` 雏形）时，向 `agent_experiences` 集合**内部写入**经验条目，字段对齐 Issue #32 pm-plan：`experienceId`（业务主键，唯一索引）、`sourcePersonaId`、`patternType`、`description`、`exampleTexts`、`effectivenessScore`、`weight`、`inheritedBy`；并将 `experienceId` 幂等追加进来源 persona 的 `ExperienceIds`。本期 SHALL NOT 提供对外 CRUD 或运营 UI（`agent_experiences` 为 #38 经验继承系统的数据源）。`effectivenessScore` 与 `weight` 首期默认 1.0。

#### Scenario: Experience written on pattern match

- **WHEN** 记忆回写识别到可沉淀模式
- **THEN** 系统 SHALL 写入一条 `agent_experiences` 条目且字段完整对齐 pm-plan
- **AND** 来源 persona 的 `ExperienceIds` SHALL 被幂等追加该 `experienceId`

#### Scenario: Idempotent experience id

- **WHEN** 同一 `experienceId` 重复写入
- **THEN** 唯一索引冲突 SHALL 被静默跳过（不抛异常、不重复）

#### Scenario: No external CRUD

- **WHEN** 本期任意请求尝试对 `agent_experiences` 发起对外 CRUD
- **THEN** 系统 SHALL NOT 暴露此类端点（仅调度器内部写）
