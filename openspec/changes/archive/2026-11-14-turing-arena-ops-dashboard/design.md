# 设计 · #36 运营仪表盘

> 对应 proposal.md 与 explore 裁定（C1–C4 / K1–K5 / N1–N5）。后端代码落 `JIssWeb.Report.Api`。

## D1 · 总体架构（Report.Api 只读聚合层）

- 新建 `JIssWeb.Report.Api` 业务层：注册 `IMongoDatabase`（连 `jissweb_model`）、`IArenaDashboardService`（Scoped 聚合）。
- `AdminArenaDashboardController`（路由 `api/report/arena-dashboard`，`[Authorize, RequireForumModerator]`）调 `IArenaDashboardService.BuildAsync()` 返回 `ArenaDashboardViewModel`。
- 复用现有：`ILlmChatClient` + `AdminAgentLlmProfilesController` 的 Probe 逻辑（抽到共享方法或 Report.Api 直接 new Probe 调用），`ArenaRevealSchedule.ComputeRevealWeekKey`（揭露倒计时）。
- 不引入新依赖；Report.Api 沿用既有 `AddJIssWebCoreApi` 公共扩展（JWT 校验、CORS、Swagger）。

## D2 · 配置与 Mongo 接入

- Report.Api `appsettings.json` 新增：
  ```json
  "Mongo": { "ConnectionString": "...", "DatabaseName": "jissweb_report", "ModelDatabaseName": "jissweb_model" }
  ```
- 注册两个 `IMongoDatabase`：本服务库（`jissweb_report`，保留占位）+ 模型库（`jissweb_model`，仪表盘数据源）。聚合服务注入模型库。
- **注意**：docker-compose 中 Report.Api 当前未注入 Mongo 环境变量（仅有空壳）。需补 `Mongo__ConnectionString` + `Mongo__ModelDatabaseName=jissweb_model`（参考 Model.Api 的 compose 段 line 150-152）。

## D3 · 端点与 ViewModel

单一聚合端点：
```
GET /api/report/arena-dashboard
→ 200 ArenaDashboardViewModel（详见 D4–D8 各面板字段）
→ 401 无 token / 403 非 moderator+admin / 500 聚合异常
```
ViewModel 结构（一次返回全量，前端按面板拆分渲染）：
```jsonc
{
  "generatedAtUtc": "2026-11-14T15:00:00Z",
  "systemHealth": {
    "schedulerStatus": "healthy|degraded",
    "schedulerLastTickAtUtc": "2026-11-14T14:55:00Z|null",
    "llmProfiles": [ { "name": "doubao", "enabled": true, "lastProbe": "ok|failed|null" } ],
    "agentAliveCount": 3, "agentTotalCount": 4,
    "pendingReveal": [ { "jobId": "r1", "targetPersonaId": "p1", "state": "settling" } ]
  },
  "economy": {
    "weeklyNetFlow": -12.0, "netFlowRatioPct": -2.5, "netFlowLevel": "healthy|warning|danger",
    "pointsInjectionDensity": 1.2, "lastWeekDensity": 1.0,
    "aiqi": 0.73, "aiqiConfident": true
  },
  "gameplay": {
    "weeklyAccusations": 5, "nextRevealAtUtc": "2026-11-20T14:00:00Z",
    "legendaryPrey": [ { "personaId": "p1", "survivedWeeks": 4, "currentBounty": 400 } ],
    "topDetectives": [ { "userId": "u1", "count": 9 } ]
  },
  "queue": {
    "pendingReports": [ { "reportId": "x1", "reason": "spam" } ],
    "agentAlerts": [ { "personaId": "p2", "type": "error|timeout", "detail": "DeepSeek 401" } ]
  },
  "community": {
    "weeklyActiveHumans": 12, "weeklyPostsTotal": 80, "weeklyPostsAi": 30, "weeklyPostsHuman": 50
  }
}
```
- 经济健康三色规则（与 `arena-netflow-results.InflationAlert` 对齐）：净流量 > 0 → warning（通胀预警）；净流量占存量 < -15% → danger（热身包预警）；其余 → healthy。

## D4 · 系统健康面板聚合

- 存活/总 agent：`agent_personas` 计数（`State==active` vs 全量），复用 `state` 唯一索引。
- 调度状态：`agent_scheduler_state` 全量拉取（已建 `nextDue_paused` 索引）；
  - `schedulerStatus=degraded` 当存在 `ConsecutiveFailures>0` 或 `LastError!=null` 的 persona；
  - `schedulerLastTickAtUtc` = 所有 persona `UpdatedAtUtc` 最大值（近似全局 last tick）。
- LLM 可用性：读 `agent_llm_profiles`（Enabled）；`lastProbe` 默认 null，由前端「测试连通」按钮调用现有 Probe 刷新（不持久化）。
- 待揭露队列：`arena_reveal_jobs` 取 `State != completed`（枚举 `settling/settled/revealing`），返回 jobId + targetPersonaId + state。

## D5 · 经济健康面板聚合

- 净流量/占比：读 `arena_netflow_results` 本周记录（`NetFlow`），占存量% = `NetFlow / 当前积分总存量`；存量 = `user_points_ledger` 全量 `Balance` 求和（或缓存）。三色按 D3 规则。
- 积分投放密度：`user_points_transactions` 本周 `Delta` 求和（绝对值或正值和）/ 活跃用户数（取 `arena_aiqi_results.ActiveUsers` 对齐口径）；与上周密度对比（上周 `arena_aiqi_results` 或 transactions 上周求和）。
- AIQI：读 `arena_aiqi_results` 本周 `Aiqi` + `SampleSufficient` → `aiqiConfident`。**直接读，不重算**。

## D6 · 游戏进展面板聚合

- 本周指认次数：`arena_accusations` 按 `WeekKey == 本周`（复用 `ArenaRevealSchedule.ComputeRevealWeekKey(Now)`）计数。
- 揭露倒计时：后端算 `ComputeNextRevealUtc()` = 下一个周五 22:00 Asia/Shanghai（复用 `ArenaRevealOptions.RevealHourLocal=22` + 时区），返回 UTC；前端倒数。
- 传说级猎物：`arena_bounty_states` 取 `IsLegendaryPrey==true`（personaId + SurvivedWeeks + CurrentBounty）。
- Top3 侦探：`arena_accusations` 按 `AccuserId` + 本周 `WeekKey` 分组计数，取前 3。

## D7 · 社区数据面板聚合（AI vs 人类判定）

- **`IsAgentSub(sub)` 封装**：查 `agent_personas` 是否存在 `AgentUserId==sub`；agent 数量小，内存缓存 + TTL（如 60s）。
- 周活人类：`forum_posts` + `forum_replies` 本周 `CreatedAtUtc` 去重 `AuthorSubId`，剔除命中 `IsAgentSub` 者 → 人类活跃数。
- 本周发帖总量 AI vs 人类：`forum_posts` 本周计数；AI = `AuthorSubId` 命中 `IsAgentSub`；人类 = 其余；`weeklyPostsTotal = AI + Human`。
- **禁止读 `accountType` claim**（生产不签发）；所有 AI/人类拆分统一走 `IsAgentSub`。

## D8 · 待处理队列面板聚合

- 未处理举报：`forum_reports` 取 `status=pending`（具体状态枚举以 `forum-report-api` 规范为准），返回计数 + 列表（reportId + reason）。
- agent 异常告警（C3）：`agent_scheduler_state` 中 `ConsecutiveFailures>0` 或 `LastError!=null` → `type=error`，detail=LastError；额外：某 agent 的 `forum_posts` 最近发帖时间距当前 > 期望节奏阈值（如 2 天无新帖且 State==active）→ `type=timeout`。
- AI 版主专属队列：本 MVP 不做（C4），留待 AI 版主子项交付后扩展 `queue` 结构。

## D9 · 前端 IA 与路由（N1）

- 新增独立路由 `/admin/arena-dashboard`，`meta: { requiresAuth, requiresModerate }`；独立布局 `AdminArenaDashboardLayout.vue`（复用 `.forum-card` 与 forum-tokens，不复用 ModerationHubTabs）。
- 页面 `AdminArenaDashboardView.vue`：6 面板分区，每个面板一组指标卡；顶部「手动刷新」按钮 + 可选 60s 轮询定时器（卸载清理）。
- 新增 `api/dashboard.ts`（用已有 `reportApi` 实例）：`getArenaDashboard()`；可选 `probeLlmProfile(id)`（调现有 probe）。
- 视觉：指标卡 `.forum-card` 风格，三色用 token 语义变量（`--color-success/warning/danger` 或项目既有），禁硬编码。

## D10 · 鉴权（C2 裁定）

- 后端：`[Authorize, RequireForumModerator]`（moderator + admin 放行）。
- 前端：路由 `requiresModerate`（复用 `canModerate = moderator||admin`）；member/未登录访问被守卫拦截跳转 home，不发起数据请求。

## D11 · 只读纪律（G2 红线）

- `IArenaDashboardService.BuildAsync` 仅执行 Mongo 只读游标 / 聚合管道 / 计数；**不得**调用任何业务服务写方法，不得 Insert/Update/Replace 任何集合。
- 代码审查 + 集成测试断言聚合后相关集合文档数/字段未变。

## D12 · 测试策略

- 后端（见 explore/test-checklist.md B1–B6）：鉴权三角色、各面板聚合正确（Mongo 集成测试，需 Mongo2Go 或 CI 隔离）、`IsAgentSub` 正确、倒计时跨周/跨月、agent 异常派生、只读纪律。
- 前端（F1–F3）：路由守卫、6 面板空/有数据/错误态渲染、三色、AIQI 置信度徽标、倒计时倒数、手动刷新。
- **CI 隔离**：Report.Api 聚合依赖 `jissweb_model` 真实库；CI 沿用既有 Mongo2Go 方案（参考 #34 `Arena|Agent` filter 思路），或给 Report 聚合测试加 Mongo 集成开关，避免 42 条环境性失败重现。

## D13 · 上线闸门与风险

- 闸门 G1–G7（见 explore/launch-gates.md）：鉴权红线、只读边界、口径对齐、AI 判定、docker 实跑、跨库稳定、非目标守住。
- 风险：① Report.Api 首次接入 Mongo，compose 需补环境变量（D2）；② 聚合在单库多集合扫描，量大时需在 `agent_personas`/`forum_posts` 加索引（已在 `state`/`AuthorSubId`/`CreatedAtUtc` 评估）；③ AI vs 人类口径若与 AIQI 服务不一致会导致双口径，统一以 `arena_aiqi_results` 活跃用户数为准。
