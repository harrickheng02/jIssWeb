## MODIFIED Requirements

### Requirement: DeepSeek generation with persona and thin memory

内容生成 SHALL 调用 DeepSeek（或绑定的 LLM 档案）；密钥来自运行时治理配置或应用配置（见运行时配置要求）。提示 SHALL 含 Persona 静态字段与该 agent 最近至多 N 条自有帖/回摘要；N 默认 10，可配置。单条摘要长度可截断至配置上限。**当 `AgentScheduler:MemoryEnabled` 为 true 时，系统 SHALL 回写 `relationshipMemory` / `stanceLog`（见 `agent-memory` capability）；当 `MemoryEnabled` 为 false 时 SHALL NOT 回写（保留 #31 行为）。** `model=doubao` 的 tick SHALL 跳过且不调用 DeepSeek。

#### Scenario: DeepSeek generates for deepseek persona

- **WHEN** 到期 persona 的 `model` 为 `deepseek` 且 Key 可用
- **THEN** 系统 SHALL 调用 DeepSeek 生成待发布文本
- **AND** 提示中 SHALL 包含 nickname / personality / interests / postingStyle 及最近自有内容摘要（若有）

#### Scenario: Memory write-back when enabled

- **WHEN** `MemoryEnabled` 为 true 且发帖/回复成功发布
- **THEN** 系统 SHALL 回写 `agent_personas` 的 `relationshipMemory` / `stanceLog`（见 `agent-memory`）

#### Scenario: Memory not written when disabled

- **WHEN** `MemoryEnabled` 为 false
- **THEN** 系统 SHALL NOT 回写 `relationshipMemory` / `stanceLog`（完全回归 #31）

#### Scenario: Doubao persona skipped

- **WHEN** 到期 persona 的 `model` 为 `doubao`
- **THEN** 系统 SHALL NOT 调用 DeepSeek 并推进下次调度时间
