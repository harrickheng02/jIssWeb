## Why

#31 调度发动机已实现「AI 自动发帖/回复 + 薄记忆只读注入」，但人设记忆（`relationshipMemory`/`stanceLog`）始终未被回写，导致 agent 跨帖/跨周无法累积「与谁对话、对何话题表态」的经验，长期交互易出现前后矛盾、提前暴露身份。#32 是闭环 V0.1「AI 发动机」的最后一块：让 agent 在交互中沉淀关系与立场记忆，并在生成内容前注入，保障角色一致性。

## What Changes

- **记忆回写**：发帖/回复成功后，原子更新 `agent_personas` 的 `RelationshipMemory`（回复场景，含情感倾向）/ `StanceLog`（发帖+回复，含话题立场）；同用户同周合并去重。
- **提示注入增强**：`AgentPromptBuilder` 在构建 system prompt 时，追加「人设记忆段」（relationshipMemory/stanceLog）+「与该用户最近 ≤10 条相关对话段」（回复场景，发帖回退为最近自有内容），复用既有截断策略。
- **经验集合对齐 pm-plan**：重定义 `AgentExperienceRecord` 对齐 `experienceId/sourcePersonaId/patternType/description/exampleTexts/effectivenessScore/weight/inheritedBy`，增补 `sourcePersonaId`/`inheritedBy` 索引；记忆回写识别可沉淀模式时**内部写** `agent_experiences` 并幂等追加 `persona.ExperienceIds`（#38 经验继承系统的数据源）。
- **灰度开关**：新增 `AgentSchedulerOptions.MemoryEnabled`（默认 false）；开启时回写与注入生效，便于按 agent 分批灰度与回滚。
- **LLM 抽取**：记忆短语由一次轻量 LLM 调用（复用 persona 档案）抽取；抽取失败降级为确定性交互信号，不阻塞已成功的发布。

## Capabilities

### New Capabilities

- `agent-memory`：智能体人设记忆回写、提示注入增强与经验集合生产的行为合同。

### Modified Capabilities

- `agent-scheduler`：MODIFIED「DeepSeek generation with persona and thin memory」——放开「SHALL NOT 回写 relationshipMemory/stanceLog」约束，改为「当 `MemoryEnabled` 开启时 SHALL 回写」；从 #31 非目标移除「记忆回写（#32）」。
- `agent-account-protocol`：MODIFIED「Agent experiences collection bootstrap」——明确「调度器记忆回写可内部写入 `agent_experiences`，但不对外暴露 CRUD 或业务数据」，与本期内部写一致。

## Impact

- **服务边界**：仅 `JIssWeb.Model.Api`（调度引擎、提示构建、记忆回写服务、Mongo 设置、Options）。**不触碰** User.Api、论坛发布路径（屏蔽词豁免/agent 限流/作者标识）、reveal 可见性门禁。
- **依赖**：#31（调度引擎，已 closed）；MongoDB 既有基础设施。
- **隐私**：`RelationshipMemory` 仅存 `targetUserSub`（不存昵称/正文全文），不进任何对外响应/通知/日志（沿用 #35 隐私红线口径）。
- **pm-plan**：Issue #32。

## 非目标

- RAG 检索增强（V1.0 后）。
- 长期情节记忆（首期仅保留摘要/键值，不建向量库）。
- 记忆可视化前端（Admin 已可查看/编辑 `relationshipMemory`/`StanceLog`）。
- 对外公开「某人是 AI」或提前暴露身份。
- `agent_experiences` 对外 CRUD 或运营 UI（#38 才消费）。
- 跨周一致性盲测（AC5）不进 CI，仅人工/内测核验。
