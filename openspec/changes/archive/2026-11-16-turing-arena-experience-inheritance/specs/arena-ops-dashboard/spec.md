## MODIFIED Requirements

> 世代存活曲线面板（Issue #38）并入运营仪表盘。鉴权、只读纪律、AI vs 人类判定均不变；本 delta 仅把面板数从 5 增至 6、前端卡片从 6 增至 7，并规定新面板数据契约。新面板的完整行为契约另见 `agent-experience-inheritance` capability。

### Requirement: 运营仪表盘只读聚合端点与鉴权

系统 SHALL 在 `JIssWeb.Report.Api` 提供 `GET /api/report/arena-dashboard` 端点，以 `[Authorize, RequireForumModerator]` 保护，返回 `ArenaDashboardViewModel`（含系统健康 / 经济健康 / 游戏进展 / 待处理队列 / 社区数据 / 世代存活曲线 六个面板的全量数据）。

系统 SHALL 对 `forumRole ∈ {moderator, admin}` 返回 200 与完整数据；对 `member` 与未登录返回 403 / 401，且不暴露任何面板数据。

端点 SHALL 为只读：不得调用任何业务服务的写方法，不得 Insert/Update/Replace 任何 MongoDB 集合（含 `agent_scheduler_state`、`user_points_*`、`arena_*`、`agent_experiences`、`agent_personas`）。

#### Scenario: Moderator 或 Admin 访问仪表盘
- **WHEN** 携带 moderator 或 admin 的合法 JWT 调用 `GET /api/report/arena-dashboard`
- **THEN** 系统返回 200 与完整的 `ArenaDashboardViewModel`（含六个面板）

#### Scenario: 普通用户访问被拒
- **WHEN** 携带 member 的合法 JWT 或匿名调用 `GET /api/report/arena-dashboard`
- **THEN** 系统返回 403 或 401，且响应体不包含任何面板数据

#### Scenario: 聚合路径不写数据
- **WHEN** 端点完成一次聚合响应
- **THEN** 所有被读取集合的文档数与字段保持不变（可通过集成测试断言）

### Requirement: 前端仪表盘页面

系统 SHALL 提供独立路由 `/admin/arena-dashboard`（`requiresModerate` 守卫），以独立布局渲染 7 面板指标卡（含新增「世代存活曲线」卡），复用 `.forum-card` 与 `forum-tokens.css` 设计变量，遵守 `.cursor/rules/forum-ui.mdc`（禁硬编码色值、标题两行截断、禁用 `:deep`）。

页面 SHALL 提供「手动刷新」按钮，并可配置轮询（默认 60s，卸载清理）；经济健康三色 SHALL 使用 token 语义色；AIQI `aiqiConfident=false` SHALL 显示「样本不足」徽标。

#### Scenario: 非授权访问前端拦截
- **WHEN** member 或未登录用户直接访问 `/admin/arena-dashboard`
- **THEN** 前端路由守卫拦截并跳转，且不发起仪表盘数据请求

#### Scenario: 面板空态与错误态
- **WHEN** 某面板无数据或聚合失败
- **THEN** 该面板显示空态或错误态，不阻塞其余面板渲染
