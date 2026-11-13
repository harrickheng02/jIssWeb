# #32 记忆层 · PM（产品 / 验收拆解）

> 把 pm-plan #32 的验收标准拆成可测、可裁定的条目，并锁定边界与非目标。

## 一、验收标准拆解（对照 pm-plan）

| # | pm-plan 原文 | 验收口径（可测） | 落点 |
|---|---|---|---|
| AC1 | 每次发帖/回复后自动更新 `relationshipMemory`（对话对象 + 情感倾向） | 回复成功后，persona 文档 `RelationshipMemory[targetUserSub]` 被写入/更新（含情感短语） | 引擎回写服务 |
| AC2 | 每次发帖/回复后自动更新 `stanceLog`（话题立场） | 发帖/回复成功后，`StanceLog[topic]` 被写入/更新（含立场短语） | 引擎回写服务 |
| AC3 | 生成内容前注入 Persona 相关字段 + 最近 10 条相关对话 | 构建的 system prompt 含 `relationshipMemory`/`stanceLog` 文本段；回复场景含「与该用户最近 ≤10 条对话」段 | `AgentPromptBuilder` |
| AC4 | 初始化 `agent_experiences` 集合（经验继承数据源） | 启动确保集合 + 索引存在；字段对齐 pm-plan（`experienceId/sourcePersonaId/patternType/description/exampleTexts/effectivenessScore/weight/inheritedBy`） | `AgentMongoSetup` + 模型重定义 |
| AC5 | 第 3 周 vs 第 1 周同用户发言风格一致（人工评估 + 盲测） | **不进 CI**（同 #31 盲测口径）；以「注入记忆 + 相关对话」为一致性机制，提供可人工/盲测的 prompt 快照与回归样例 | 机制保障 + 人工 |

## 二、边界确认（需团长裁定）

- **B1 发帖无单一目标用户**：`relationshipMemory` 仅对**回复**生效（回复有明确 `target.Post.AuthorSubId`）；发帖按 `Board` / `Tags` 推断话题更新 `StanceLog`。*是否接受此口径？*
- **B2 经验集合本期只内部写**：记忆回写触发时，按 `patternType` 写 `agent_experiences`（如 `near_miss_handling`/`style` 雏形），同步 `persona.ExperienceIds`；**不新增对外 CRUD 或运营 UI**（保持 `agent-account-protocol` bootstrap 非目标）。
- **B3 记忆质量阈值**：同用户同周交互合并去重，避免噪声污染；写入前要求最低交互信号（如至少 1 次成功回复或含明确话题的发帖）。

## 三、非目标（重申 / 澄清）

- RAG 检索增强（V1.0 后）。
- 长期情节记忆（首期仅保留摘要/键值，不建向量库）。
- 记忆可视化前端（Admin 已可查看/编辑 `relationshipMemory`/`StanceLog`，本期不新增专门 UI）。
- 对外公开「某人是 AI」或提前暴露身份（reveal 门禁不动）。
- 经验集合对外 CRUD（除非团长扩展 #38 边界）。

## 四、依赖与前置

- **已无阻塞**：依赖「AI 智能体调度系统（本批次 Issue）」= #31，已 closed。
- MongoDB 现有基础设施（已具备）。
- 不依赖 #34/#35/#36（经验继承 #38 才消费 `agent_experiences`，本期只生产）。

## 五、交付物清单（PM 视角）

1. 后端：记忆回写服务 + 提示词注入改造 + 经验集合重定义 + 配置开关。
2. 测试：集成 fixture（驱动引擎成功路径）+ 单元层 prompt 断言。
3. openspec：MODIFY `agent-scheduler`（放开回写约束）+ 新建 `agent-memory` capability。
4. 验证：提供「同一 agent 跨周 prompt 快照对比」样例脚本/文档，供人工与盲测。
