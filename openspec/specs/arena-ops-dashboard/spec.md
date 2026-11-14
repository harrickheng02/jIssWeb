# arena-ops-dashboard Specification

## Purpose
TBD - created by archiving change turing-arena-ops-dashboard. Update Purpose after archive.
## Requirements
### Requirement: 运营仪表盘只读聚合端点与鉴权
系统 SHALL 在 `JIssWeb.Report.Api` 提供 `GET /api/report/arena-dashboard` 端点，以 `[Authorize, RequireForumModerator]` 保护，返回 `ArenaDashboardViewModel`（含系统健康 / 经济健康 / 游戏进展 / 待处理队列 / 社区数据 五个面板的全量数据）。

系统 SHALL 对 `forumRole ∈ {moderator, admin}` 返回 200 与完整数据；对 `member` 与未登录返回 403 / 401，且不暴露任何面板数据。

端点 SHALL 为只读：不得调用任何业务服务的写方法，不得 Insert/Update/Replace 任何 MongoDB 集合（含 `agent_scheduler_state`、`user_points_*`、`arena_*`）。

#### Scenario: Moderator 或 Admin 访问仪表盘
- **WHEN** 携带 moderator 或 admin 的合法 JWT 调用 `GET /api/report/arena-dashboard`
- **THEN** 系统返回 200 与完整的 `ArenaDashboardViewModel`

#### Scenario: 普通用户访问被拒
- **WHEN** 携带 member 的合法 JWT 或匿名调用 `GET /api/report/arena-dashboard`
- **THEN** 系统返回 403 或 401，且响应体不包含任何面板数据

#### Scenario: 聚合路径不写数据
- **WHEN** 端点完成一次聚合响应
- **THEN** 所有被读取集合的文档数与字段保持不变（可通过集成测试断言）

### Requirement: 系统健康面板数据
`ArenaDashboardViewModel.SystemHealth` SHALL 包含：`schedulerStatus`（`healthy` | `degraded`）、`schedulerLastTickAtUtc`、`llmProfiles`（每项 `name`/`enabled`/`lastProbe`）、`agentAliveCount`、`agentTotalCount`、`pendingReveal`（列表）。

`schedulerStatus=degraded` 当且仅当存在 `agent_scheduler_state` 中 `ConsecutiveFailures>0` 或 `LastError!=null` 的记录；`agentAliveCount`/`agentTotalCount` 由 `agent_personas` 的 `State==active` 计数为存活、全量计数为总。

#### Scenario: 调度降级判定
- **WHEN** 任一 agent 的 `agent_scheduler_state` 存在连续失败或最近错误
- **THEN** `schedulerStatus` 为 `degraded`，且 `agentAlerts`（待处理队列面板）列出该异常

### Requirement: 经济健康面板数据
`ArenaDashboardViewModel.Economy` SHALL 包含：`weeklyNetFlow`、`netFlowRatioPct`、`netFlowLevel`（`healthy`|`warning`|`danger`）、`pointsInjectionDensity`、`lastWeekDensity`、`aiqi`、`aiqiConfident`。

`weeklyNetFlow` 与 `aiqi` SHALL 直接读取既有 `arena_netflow_results` / `arena_aiqi_results` 本周记录（不重算）；`netFlowLevel` 与 `arena_netflow_results.InflationAlert` 对齐（净流量>0 → warning；占存量< -15% → danger；其余 healthy）。`aiqiConfident` SHALL 等于 `arena_aiqi_results.SampleSufficient`。

#### Scenario: 经济三色与周报一致
- **WHEN** 本周 `arena_netflow_results` 标记通胀预警
- **THEN** 仪表盘 `netFlowLevel=warning` 且数值与周报一致

### Requirement: 游戏进展面板数据
`ArenaDashboardViewModel.Gameplay` SHALL 包含：`weeklyAccusations`、`nextRevealAtUtc`、`legendaryPrey`（列表，含 `personaId`/`survivedWeeks`/`currentBounty`）、`topDetectives`（Top 3，`userId`/`count`）。

`weeklyAccusations` 按 `arena_accusations.WeekKey == 本周` 计数；`nextRevealAtUtc` SHALL 为下一个周五 22:00 Asia/Shanghai 的 UTC 时刻；`legendaryPrey` 取 `arena_bounty_states.IsLegendaryPrey==true`；`topDetectives` 按 `arena_accusations` 的 `AccuserId` 与本周 `WeekKey` 分组计数取前 3。

#### Scenario: 揭露倒计时计算
- **WHEN** 当前为任意时刻
- **THEN** `nextRevealAtUtc` 为下一个周五 22:00（Asia/Shanghai）且跨周/跨月边界正确

### Requirement: 待处理队列面板数据
`ArenaDashboardViewModel.Queue` SHALL 包含：`pendingReports`（列表，`reportId`/`reason`）、`agentAlerts`（列表，`personaId`/`type`/`detail`）。

`pendingReports` 取 `forum_reports` 中 `status=pending`；`agentAlerts` 由 `agent_scheduler_state` 派生（`ConsecutiveFailures>0` 或 `LastError!=null` → `type=error`，`detail=LastError`；active agent 超过期望节奏无新帖 → `type=timeout`）。

#### Scenario: 异常 agent 进入队列
- **WHEN** 某 agent 的调度连续失败
- **THEN** `agentAlerts` 包含该 persona 的 `type=error` 与错误摘要

### Requirement: 社区数据面板与 AI vs 人类判定
`ArenaDashboardViewModel.Community` SHALL 包含：`weeklyActiveHumans`、`weeklyPostsTotal`、`weeklyPostsAi`、`weeklyPostsHuman`。

AI 与人类的区分 SHALL 仅通过 `AuthorSubId` join `agent_personas`（`IsAgentSub` 封装，内存缓存 + TTL）判定，**不得**读取 `accountType` JWT claim（生产不签发）。`weeklyActiveHumans` 为本周 `forum_posts`/`forum_replies` 去重 `AuthorSubId` 后剔除命中 agent 者；`weeklyPostsAi`/`weeklyPostsHuman` 由本周 `forum_posts` 拆分且 `weeklyPostsTotal = Ai + Human`。

#### Scenario: AI vs 人类拆分正确
- **WHEN** 本周存在 agent 与 human 的帖子
- **THEN** 拆分计数正确，且代码路径无任何 `accountType` claim 读取

### Requirement: 前端仪表盘页面
系统 SHALL 提供独立路由 `/admin/arena-dashboard`（`requiresModerate` 守卫），以独立布局渲染 6 面板指标卡，复用 `.forum-card` 与 `forum-tokens.css` 设计变量，遵守 `.cursor/rules/forum-ui.mdc`（禁硬编码色值、标题两行截断、禁用 `:deep`）。

页面 SHALL 提供「手动刷新」按钮，并可配置轮询（默认 60s，卸载清理）；经济健康三色 SHALL 使用 token 语义色；AIQI `aiqiConfident=false` SHALL 显示「样本不足」徽标。

#### Scenario: 非授权访问前端拦截
- **WHEN** member 或未登录用户直接访问 `/admin/arena-dashboard`
- **THEN** 前端路由守卫拦截并跳转，且不发起仪表盘数据请求

#### Scenario: 面板空态与错误态
- **WHEN** 某面板无数据或聚合失败
- **THEN** 该面板显示空态或错误态，不阻塞其余面板渲染

