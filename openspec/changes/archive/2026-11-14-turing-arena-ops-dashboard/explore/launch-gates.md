# 上线闸门 · #36 运营仪表盘

> 合并前必须逐条确认（G1–G6）。标注 owner 与验证方式。

## G1 · 鉴权正确性（安全红线，C2 裁定：版主 + admin）
- admin / moderator 访问 `/admin/arena-dashboard` 返回 200 且数据完整。
- member / 未登录访问：后端端点 403、前端路由守卫拦截跳转（不暴露任何面板数据）。
- 验证：集成测试三类角色（admin✓ / moderator✓ / member✗）；前端 `requiresModerate` 守卫单测。

## G2 · 只读边界（数据纪律）
- 仪表盘端点（Report.Api）**零写操作**：不调用任何业务服务的写方法，不更新任何集合（含 `agent_scheduler_state`、`user_points_*`、`arena_*`）。
- 验证：代码审查 + 集成测试断言聚合路径不触发写；人工 grep 端点实现确认无 `Insert/Update/Replace`。

## G3 · 数据正确性抽样（口径对齐）
- 面板 B「AIQI / 净流量」直接读 `arena_aiqi_results` / `arena_netflow_results`，与 `ArenaAiqiBatchService` 周报数值一致。
- 面板 C「本周已指认次数」「面板 E 活跃用户数」与 AIQI 服务口径对齐（同源 `arena_points_events` / 活跃侦探数）。
- 验证：docker 实跑一轮 AIQI 周报后，仪表盘数值与之比对。

## G4 · AI vs 人类判定正确（约束验证）
- 面板 E「发帖 AI vs 人类拆分」「周活人类」用 `IsAgentSub` join `agent_personas`，不读 `accountType` claim。
- 验证：构造 1 agent + 1 human 发帖，断言拆分计数正确；确认代码无 `accountType` 读取。

## G5 · 全面板 docker 实跑渲染
- 本地 `docker compose` 起全套 + 前端 dev，admin 账号登录后 6 面板均有数据/空态正确渲染，无 500。
- 验证：人工点验（巡检 ≤15 分钟自测记录）。

## G6 · 跨库读取稳定
- Report.Api 连 Model + Accounting 两个 Mongo 库在 docker / CI 下均可用；CI 无 `accountType` 相关失败。
- 验证：CI 跑通（Report.Api 端点需 Mongo 可用；CI 用自包含 Mongo2Go 或跳过 Report 套件见备注）。
- **备注**：后端测试若依赖真实 Mongo，CI 需沿用既有 Mongo2Go 方案；若 Report.Api 聚合依赖 Accounting 库，CI 隔离策略需确认（参考 #34 的 `Arena|Agent` filter 思路，必要时给 Report 聚合加 Mongo 集成测试开关）。

## G7 · 非目标守住
- 无 SignalR/推送；无公开大屏；AI 版主专属队列不在本 PR（归其独立子项）。
- 验证：代码审查确认。
