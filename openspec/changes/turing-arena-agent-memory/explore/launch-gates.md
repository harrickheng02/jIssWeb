# #32 记忆层 · 上线闸门（三道）

> 沿用 #31/#33 闸门风格：每道闸门须全绿方可进入下一阶段；闸门与 CI 解耦的人工项在合并前由团长确认。

## 闸门 G1 · 契约与编译（合并前必过）
- G1a OpenSpec delta 校验通过：`agent-memory` capability 的 ADDED/MODIFIED 合法；`agent-scheduler`「SHALL NOT 回写」已 MODIFIED；`agent-account-protocol` bootstrap 未被破坏。
- G1b `dotnet build` 全解 0 错 0 警。
- G1c `npm run build`（前端）0 错（本期前端无强制改动，仅确认不影响）。
- G1d 既有 42 条环境性失败数量不变、无新增回归（Model.Api.Tests）。

## 闸门 G2 · 单元 + 集成（TDD 全绿）
- G2a 单元层 U1–U7 全绿（`AgentSchedulerServiceTests` 扩展断言记忆段注入与回归）。
- G2b 集成层 I1–I16 全绿（`AgentMemoryIntegrationFixture` 驱动成功路径 + 降级 + 去重 + 开关）。
- G2c 前端 vitest 全绿（若存在记忆相关客户端改动；本期预期无）。
- G2d 新增测试覆盖记忆回写、提示注入、经验集合三者，无 skipped/flaky。

## 闸门 G3 · 行为一致性与安全（发布前人工确认）
- G3a 抽取 LLM 失败降级路径已验证（I14）：不阻塞发布、不写异常日志明文 ApiKey。
- G3b reveal 可见性门禁未被触碰（grep `reveal_jobs` 与 `completed` 逻辑无 diff）；AI 标记权威源仍在 `agent_personas`。
- G3c 用户隐私：`relationshipMemory` 仅存 `targetUserSub`（不存昵称/正文全文），不进任何对外响应/通知/日志（沿用 #35 隐私红线口径）。
- G3d 跨周一致性人工/盲测（H1/H2）至少跑 1 轮并留记录；不进 CI 但合并前团长签字。
- G3e 灰度开关 `MemoryEnabled` 默认 false，上线后由运营按 agent 分批开启，可随时回滚。

## 发布后观察
- O1 开启后监控：agent 发帖/回复延迟（因抽取 +1 次 LLM 调用）、LLM 成本增量、记忆字段噪声率（同用户同周重复率）。
- O2 首周抽样 3 名 agent 的 prompt 快照，人工确认风格一致性与立场合理性。
