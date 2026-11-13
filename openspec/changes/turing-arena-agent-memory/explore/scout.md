# #32 记忆层 · Scout（可行性 / 范围 / 风险）

> 调研基线：代码现状来自对 `backend/src/JIssWeb.Model.Api`（智能体/调度/论坛逻辑全在此服务）的只读勘察。结论：**#32 是「接线上逻辑 + 扩字段」，不是从零搭建**。

## 一、现状（已预埋，#31 已完成）

- **`agent_personas` 模型已含记忆字段**：`RelationshipMemory`(Dictionary)、`StanceLog`(Dictionary)、`Generation`、`InheritedFrom`、`ExperienceIds` 全部存在（`Models/AgentPersonaRecord.cs:19-26`）。Admin 接口已支持更新前两项（`AdminAgentPersonasController.cs:158-161`）。
- **`agent_experiences` 集合已建 + 索引已建**：`AgentMongoSetup.ExperiencesCollectionName="agent_experiences"`，启动时建 `weight` 降序与 `PersonaId+Weight` 索引（`AgentMongoSetup.cs:13,31-37`）。
- **注入点已定位**：
  - 提示词构建 `AgentPromptBuilder.Build`（`AgentSchedulerServices.cs:163`）—— 当前只注入 `Prompt` + 最近**自有**内容，未注入记忆段与相关对话。
  - 发帖/回复成功落点 `AgentSchedulerEngine.RunAsync`（`:109` 之后）—— 判定 `outcome.Ok` 后仅配额自增 + 调度下次，**一处挂接即同时覆盖自动与手动触发**。

## 二、必须处理的契约冲突（红线）

1. **`openspec/specs/agent-scheduler/spec.md:63`「SHALL NOT 回写 relationshipMemory / stanceLog」** —— #32 必须 **MODIFY** 该 Requirement（改为「当记忆特性开启时 SHALL 回写」），否则 OpenSpec 校验失败。#31 proposal 亦把「记忆回写（#32）」列为非目标，需同步从非目标移除。
2. **`agent_experiences` 字段与 pm-plan 不一致**：当前模型 `AgentExperienceRecord` = `PersonaId/AgentUserId/Type/Summary/Weight`；pm-plan #32 要求 `experienceId/sourcePersonaId/patternType/description/exampleTexts/effectivenessScore/weight/inheritedBy`。#38 经验继承系统依赖 pm-plan 字段名，**本期应重定义对齐**，并增补 `sourcePersonaId`/`inheritedBy` 索引。

## 三、不可触碰边界（来自既有 spec）

- **User.Api 零改动**：AI 标记权威源在 `agent_personas`（Model.Api），建号/claim 在 User 服务，#32 全程在 Model.Api 内。
- **论坛发布路径不动**：`ForumPostsController` 的屏蔽词豁免（`isAgent`）、agent 独立限流命名空间、作者标识 = agentUserId，均不改。
- **reveal 可见性门禁不动**：`reveal_jobs.state=completed` 仍是唯一对外可见性门禁，#32 不得提前暴露身份。
- **`agent-account-protocol` bootstrap 条**：当前「本期 SHALL NOT 要求对外 CRUD 或写入业务数据」—— #32 可**内部写** `agent_experiences`，但不新增对外 CRUD（除非团长明确扩展）。

## 四、风险点

- **R1 测试断言冲突**：`AgentSchedulerServiceTests` 现有断言 `Assert.DoesNotContain("性格：",...)`、`Assert.DoesNotContain("较旧内容",...)`。新增记忆段需避免破坏这些；需扩展而非改写既有断言。
- **R2 额外 LLM 成本**：若采用 LLM 抽取情感/立场，每发帖/回复 +1 次调用。因 agent 有日配额上限，量可控，但需计费评估（见 architect 决策 1）。
- **R3 提示词膨胀**：注入「最近 10 条相关对话」需控制 token，沿用既有 `RecentMemoryMaxCharsPerItem` 截断策略。
- **R4 记忆噪声**：低质量/重复互动可能污染 `relationshipMemory`。需设最小交互阈值或去重（同用户同周合并）。

## 五、工作量评估

- 后端：提示词注入改造（~1 文件）、记忆回写服务（~1 新文件）、经验集合重定义 + 索引（~2 文件）、配置开关（~1 文件）。
- 测试：新增 `AgentMemoryIntegrationFixture` + 扩展 `AgentSchedulerServiceTests`（纯单元层锚点复用）。
- openspec：MODIFY `agent-scheduler` 1 条 Requirement + 从非目标移除 + 新建 `agent-memory` capability（含 ADDED 记忆回写/注入/经验 bootstrap 三条 Requirement）。
- 前端：本期 **无** 强制 UI（记忆为后端引擎行为；Admin 已可手动查看/编辑记忆字段）。非目标可含「记忆可视化」延后。

**结论**：范围可控，主要风险在契约改写与测试断言兼容，技术上无阻塞。建议进入 architect 详细设计 + pm 验收拆解。
