# 提案 · #36 运营仪表盘（图灵场 V0.3 — 可运营）

> OpenSpec change：`turing-arena-ops-dashboard`。Issue #36（GitHub OPEN，pm-plan 已定义）。
> 阶段：explore 已收口（4 项关键裁定 C1–C4 已确认），本文件为 propose 阶段产物。

## Why（为什么做）

图灵场 V0.3 的核心目标是「单人晚间可维护」——让运营者每日 ≤15 分钟完成所有运营巡检。当前 AI 调度状态、LLM 可用性、存活 agent、积分经济、AIQI、指认/揭露进展、传说级猎物、社区数据等运营信号散落在 `agent_personas` / `agent_scheduler_state` / `arena_*` / `user_points_*` / `arena_aiqi_results` / `forum_*` 等集合与各自服务中，运营者需翻多个后台才能掌握全局，效率低、易遗漏异常。本提案聚合为一个 admin/moderator 可见的只读仪表盘，统一呈现 6 大面板。

## 目标

- 单一页面 `/admin/arena-dashboard`，`forumRole ∈ {moderator, admin}` 可见。
- 6 面板一次性只读聚合：系统健康 / 经济健康 / 游戏进展 / 待处理队列 / 社区数据 / 巡检效率。
- 轮询 + 手动刷新，无实时推送。
- 后端单一 `GET /api/report/arena-dashboard` 返回全量 `ArenaDashboardViewModel`。

## 范围（做什么）

- **后端（Report.Api）**：新增 `AdminArenaDashboardController` + 聚合服务，连 `jissweb_model` 单库（Model/Accounting 数据同库）只读聚合；封装 `IsAgentSub` 判定；复用现有 LLM `Probe`；后端算 `nextRevealAtUtc`。
- **前端**：独立路由 + 独立布局，6 面板指标卡，复用 `.forum-card` 与 `forum-tokens.css`，遵守 `forum-ui.mdc`。
- **数据口径**：AIQI / 净流量直接读 `arena_aiqi_results` / `arena_netflow_results`（不重算）；活跃用户/指认次数与 `arena-aiqi-metric` 规范对齐。

## 非目标（不做什么）

- 公开社区数据大屏。
- 实时推送（SignalR/WebSocket）。
- AI 版主专属「疑难举报」队列（AI 版主为 V0.3 独立子项，尚未交付；MVP 待处理队列收敛为「未处理举报 + agent 异常告警」）。
- 结构化 agent 健康事件表（`agent_health_events` 列为 V0.3+，MVP 从 `agent_scheduler_state` 派生）。
- 任何写操作（仪表盘纯只读）。

## 服务边界

- **Report.Api** 为只读聚合层：连接 `jissweb_model`（通过 `Mongo__ConnectionString` + 新增 `ModelDatabaseName=jissweb_model` 配置），读取 agent/arena/forum/accounting 各集合，组装 ViewModel 返回。
- **绝不写**任何业务集合；写操作仍归 Model.Api / Accounting.Api 各业务服务。
- 跨服务但同库：Model.Api 与 Accounting.Api 写入的数据都在 `jissweb_model`，Report.Api 一处读取。

## UI 约束

- 遵循 `.cursor/rules/forum-ui.mdc`：`forum-tokens.css` 变量、8px 网格、标题/摘要最多两行截断、禁用 `:deep()`、优先 `<style scoped>`。
- 指标卡复用 `.forum-card` 风格；经济健康三色（健康/预警/危险）用 token 语义色，不硬编码。
- 路由 `requiresModerate`（版主+admin）；非授权访问前端守卫拦截。

## 已确认裁定（详见 explore/decisions.md）

- **C1** 端点宿主 = `Report.Api`（连 `jissweb_model` 单库只读聚合）。
- **C2** 鉴权 = `RequireForumModerator`（版主 + admin 可见，团长裁定覆盖 Issue 原文「仅 admin」）。
- **C3** agent 异常 = 从 `agent_scheduler_state` 派生当前异常（+ forum_posts 推断超时未发帖）。
- **C4** 待处理队列 = 未处理举报 + agent 异常告警（AI 版主队列留待其交付）。
- **K1–K5** 已确认约束；**N1–N5** 默认采纳（独立路由 / LLM 手动探针 / 单聚合端点 / 后端算倒计时 / 周活发帖 join agent_personas）。

## 验收（对齐 GitHub Issue #36）

- admin/moderator 可见独立页面 `/admin/arena-dashboard`；6 面板数据/空态正确；巡检 ≤15 分钟自测记录。
- 非目标项不出现；端点是只读（G2 闸门）。
