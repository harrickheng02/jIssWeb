# 任务清单 · #36 运营仪表盘

> 状态：T1–T18 已闭环（后端基础设施 → 聚合服务+端点 → 前端 → 测试 → 本地验证全绿 → 提交+推送+PR #49）；T19–T20 远程写待团长逐次授权。
> 遵循 spec-driven：TDD + Construction SOP。远程写（提交/推送/PR/合并/archive/pm:push）逐次点头。

## Phase 0 · explore（已完成）
- [x] T0. 技术侦察 + 6 面板数据源映射；4 项关键裁定 C1–C4 确认；产出 proposal/design/tasks + delta spec。

## Phase 1 · 后端基础设施（Report.Api 接入）
- [x] T1. `Report.Api/appsettings.json` 补 `Mongo.ModelDatabaseName=jissweb_model`；`docker-compose.yml` 补 Report.Api 的 `Mongo__ConnectionString` + `Mongo__ModelDatabaseName`。
- [x] T2. Report.Api `Program.cs` 注册模型库 keyed `IMongoDatabase("arena-dashboard-model")` + `IArenaDashboardService`(Scoped) + 复用 `AddJIssWebCoreApi`；为解耦不引用 Model.Api，自包含 `RequireForumModeratorAttribute` 与 `ArenaDashboardSchedule` 纯函数。
- [x] T3. 新建 `ArenaDashboardModels.cs`：`ArenaDashboardViewModel` 及 SystemHealth/Economy/Gameplay/Queue/Community 子结构（字段对齐 design D3）。
- [x] T4. 新建 `IsAgentSubResolver`：查 `agent_personas.AgentUserId`，内存缓存 + TTL（60s）。

## Phase 2 · 后端聚合服务 + 端点
- [x] T5. `ArenaDashboardService` 系统健康面板：agent 存活/总计数、scheduler 状态/lastTick、llmProfiles、pendingReveal。
- [x] T6. 经济健康面板：读 `arena_netflow_results`（净流量+三色）、`user_points_*`（存量+密度，与上周对比）、`arena_aiqi_results`（AIQI+置信度，不重算）。
- [x] T7. 游戏进展面板：本周指认次数（`WeekKey`）、Top3 侦探、`arena_bounty_states` 传说级、`ComputeNextRunUtc`（下个周五 22:00 Asia/Shanghai）。
- [x] T8. 社区数据面板：`forum_posts`/`forum_replies` 本周周活人类（剔除 agent）+ 发帖 AI vs 人类拆分（用 `IsAgentSub`）。
- [x] T9. 待处理队列面板：`forum_reports` pending + `agent_scheduler_state` 异常派生（error/timeout）。
- [x] T10. `AdminArenaDashboardController`：`[Authorize, RequireForumModerator]`，`GET /api/report/arena-dashboard` 返回 ViewModel。

## Phase 3 · 前端
- [x] T11. 路由 `/admin/arena-dashboard`（`requiresModerate` 守卫已支持）+ 复用 HeaderUserMenu 菜单项。
- [x] T12. `api/clients.ts`：`getArenaDashboard()`（用既有 `reportApi` 实例）+ 全套面板 DTO 接口（camelCase）。
- [x] T13. `AdminArenaDashboardView.vue`：6 面板渲染，三色语义、AIQI 置信度徽标、揭露倒计时（1s 轮询 now 计算）、手动刷新 + 60s 轮询（onUnmounted 清理）；遵守 `forum-ui.mdc`。

## Phase 4 · 测试
- [x] T14. 后端单测/集成测试：鉴权三角色、`IsAgentSub`、各面板聚合正确、倒计时跨周跨月、agent 异常派生、只读纪律（聚合后集合未变）——共 17 条全绿（`JIssWeb.Report.Api.Tests`）。
- [x] T15. 前端单测：新增 `clients.arena-dashboard.test.ts`（API 契约 + 5 面板形态断言）；全量 61 条 vitest 通过。
- [x] T16. docker 实跑：经网关 5094 冒烟——匿名 401 / member 403 / moderator 200；moderator 返回真实聚合数据（5 面板字段齐全、nextRevealAtUtc 正确、待处理队列含真实举报与 agent 告警）。

## Phase 5 · change-review / 远程写（逐次点头）
- [x] T17. `openspec validate --all` 62/62 绿；后端全解 solution 0 错 0 警；前端 vue-tsc + build 通过；代码注释与中文提交信息就绪（提交待 T18）。
- [x] T18. 提交 + 推送 + 开 PR（commit `5b6a894` + 分支 `feat/arena-ops-dashboard` + PR #49 → master）。
- [x] T19. 合并 PR #49（`gh api PUT .../merge` merge 方式，merge commit `2ae8aba`）+ 本地 `git switch master && git pull --ff-only` 同步（HEAD=`2ae8aba`）+ master CI 复查（run `43618192484` **success**：后端 Arena suite + Report.Api suite + 前端全绿；仅 Node20 弃用 / git128 post-job 无害警告）。
- [ ] T20. `openspec archive turing-arena-ops-dashboard --yes` + pm-plan #36 `state:closed` + `npm run pm:push`（Issue #36 CLOSED）+ 更新项目记忆（待团长授权）。

## 依赖与衔接
- 依赖 #35（积分账本/AIQI 落点就绪）、#31（调度）、#34（悬赏/猎手）、现有治理台与鉴权链路。
- 不依赖 AI 版主（其专属队列归独立子项，本 MVP 不含，见 C4）。
- CI 隔离：Report.Api.Tests 用 Mongo2Go 内存实例（零外部依赖），沿用 #34 `Arena|Agent` filter 思路；需在 ci.yml 增加独立门禁步骤（见 T18 提交一并补）。
