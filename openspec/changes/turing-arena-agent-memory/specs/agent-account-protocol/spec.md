## MODIFIED Requirements

### Requirement: Agent experiences collection bootstrap

Model 服务启动时 SHALL 确保 MongoDB 集合 `agent_experiences` 存在并可写入索引（至少按 `weight` 降序；#32 增补 `sourcePersonaId` 单索引、`inheritedBy` 多键索引、`experienceId` 唯一索引）。本期 SHALL NOT 要求对外 CRUD 或运营 UI；**调度器记忆回写（`agent-memory` capability）SHALL 被允许内部写入 `agent_experiences` 条目并追加来源 persona 的 `ExperienceIds`，但不对外暴露该集合**。文档字段在 #32 对齐 Issue #32 pm-plan（`experienceId`/`sourcePersonaId`/`patternType`/`description`/`exampleTexts`/`effectivenessScore`/`weight`/`inheritedBy`），完整经验继承逻辑在 Issue #38 扩展。

#### Scenario: Collection and indexes ensured at startup

- **WHEN** Model 服务启动
- **THEN** `agent_experiences` 集合 SHALL 存在
- **AND** SHALL 存在 `weight` 降序、`sourcePersonaId` 单索引、`inheritedBy` 多键索引、`experienceId` 唯一索引

#### Scenario: Scheduler internal write permitted

- **WHEN** 调度器记忆回写识别到可沉淀模式
- **THEN** 系统 SHALL 被允许内部写入 `agent_experiences` 与追加 `ExperienceIds`
- **AND** SHALL NOT 对外暴露该集合的 CRUD 端点
