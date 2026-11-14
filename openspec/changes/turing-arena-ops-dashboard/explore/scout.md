# 技术侦察 · #36 运营仪表盘

> 范围：仅读取/搜索，未改动任何代码。路径为绝对路径，行号取自 2026-11-14 现场。
> 目的：摸清仪表盘 6 个面板的数据源、可复用的管理与前端基础设施、以及缺口，为 architect / decisions 提供依据。

## 一、总览：6 面板 → 数据源映射

| 面板（Issue #36 验收） | 数据源集合 | 宿主服务 | 现成度 |
|---|---|---|---|
| 系统健康：AI 调度服务状态 | `agent_scheduler_state`（NextDueAtUtc / ConsecutiveFailures / LastError） | Model.Api | 可派生，无全局心跳 |
| 系统健康：LLM API 可用性 | `agent_llm_profiles`（Enabled）+ 现有 `Probe` 逻辑 | Model.Api | 可复用 |
| 系统健康：本周存活/总 agent 数 | `agent_personas`（State=active vs 全部） | Model.Api | 直接计数 |
| 系统健康：待揭露队列 | `arena_reveal_jobs`（State≠completed） | Model.Api | 直接查 |
| 经济健康：积分净流量/占比 | `arena_netflow_results`（NetFlow/InflationAlert） | Accounting.Api | 直接读（周日止跑批） |
| 经济健康：积分投放密度 | `user_points_transactions` 本周 Delta 求和 / 活跃用户数 | Accounting.Api | 聚合 |
| 经济健康：AIQI 与置信度 | `arena_aiqi_results`（Aiqi/SampleSufficient） | Accounting.Api | 直接读 |
| 游戏进展：本周已指认次数 | `arena_accusations`（WeekKey=本周） | Model.Api | 聚合 |
| 游戏进展：距下次揭露倒计时 | `ArenaRevealSchedule` + RevealHourLocal=22 / 周五 | Model.Api | 需计算 |
| 游戏进展：传说级猎物列表 | `arena_bounty_states`（IsLegendaryPrey=true） | Model.Api | 直接查 |
| 游戏进展：本周最活跃侦探 Top3 | `arena_accusations` 按 AccuserId+WeekKey 分组 | Model.Api | 聚合（无端点） |
| 社区数据：周活跃人类用户数 | `forum_posts`/`forum_replies` 本周 + join `agent_personas` | Model.Api | 聚合（无端点） |
| 社区数据：本周发帖总量 AI vs 人类 | 同上拆分 | Model.Api | 聚合（无端点） |
| 待处理队列：未处理举报 | `forum_reports`（status=pending） | Model.Api | 直接查 |
| 待处理队列：agent 异常告警 | `agent_scheduler_state`（ConsecutiveFailures>0 / LastError!=null） | Model.Api | 可派生，无历史表 |

## 二、管理员鉴权（零成本复用）

- JWT 三级 `forumRole`：`member` / `moderator` / `admin`（`backend/.../Common/Security/ForumRoleClaim.cs:6-11`）。
- 鉴权特性：`RequireForumAdminAttribute`（仅 Admin）、`RequireForumModeratorAttribute`（Moderator+Admin），见 `Model.Api/Authorization/ForumRoleAuthorizationFilters.cs:9-36`。
- 前端：`stores/auth.ts:14` 定义 `ForumRole`，`:138-148` 算 `canAdmin`；`router/index.ts` 的 `beforeEach`（:164-172）已支持 `requiresAdmin` meta。
- **结论**：新建 dashboard 控制器用 `[Authorize, RequireForumAdmin]`；前端新增 `requiresAdmin` 路由即可，无需新建鉴权机制。

## 三、AI 智能体与调度（系统健康面板）

- 调度引擎 `Model.Api/Services/AgentSchedulerEngine.cs`，自研 `BackgroundService` + `agent_scheduler_state`（`Models/AgentSchedulerStateRecord.cs`），**无 Hangfire**。
- `agent_personas` 状态枚举 `active`/`eliminated`/`archived`（`AgentPersonaRecord.cs:40-51`）；`state` 唯一索引已建（`AgentMongoSetup.cs:24-26`）。
- LLM 探测端点已存在：`AdminAgentLlmProfilesController.cs:119-142`（`POST .../probe`，调 `ILlmChatClient.CompleteAsync` 发「仅回复 OK」做连通性检测）→ 仪表盘可直接复用或抽取。
- **缺口**：① 无全局调度心跳（只有 NextDueAtUtc，无 last-success-tick 总览）；② 无结构化异常告警表（异常仅存各 persona 的 `LastError` 字符串 + 应用日志 `LogWarning/LogError`）。

## 四、游戏机制与经济（游戏/经济面板）

- 集合名见 `ArenaMongoSetup.cs:16-27`：`arena_reveal_jobs`、`arena_accusations`、`arena_target_tallies`、`arena_bounty_states`、`arena_bounty_runs`、`arena_bounty_medals`、`arena_hunter_badges`。
- 揭露状态机 `settling→settled→revealing→completed`（`ArenaRevealJobRecord.cs:9-20`；`PublishedAtUtc` 为可见性门禁）。
- 积分账本落在 **Accounting.Api**（非 Model.Api）：`user_points_ledger` + `user_points_transactions`（`Accounting.Api/Mongo/AccountingMongoSetup.cs:12-13`）。
- AIQI/净流量已落点：`arena_aiqi_results`、`arena_netflow_results`，由 `Accounting.Api/Services/ArenaAiqiBatchService.cs` 周日止幂等跑批（按 weekKey）→ **仪表盘直接读结果，不重算**。
- 悬赏/猎手：`arena_bounty_states.IsLegendaryPrey`、`arena_hunter_badges`（终身一条 turing-hunter）。

## 五、报表服务（仪表盘端点宿主候选）

- `JIssWeb.Report.Api` 当前为空壳：`Program.cs` 仅基础 host 无业务 DI；`Controllers/SampleController.cs` 仅回显；端口 5101。
- Issue #6 明确写：「`JIssWeb.Report.Api`（或 BFF 占位路由）提供至少一个只读端点…该接口同时服务图灵场运营仪表盘的社区数据面板」→ **Report.Api 是指定宿主**。
- `forum-report-api` 规范仅举报工单 CRUD/审核，**非统计端点**，不可直接复用。
- Model.Api 已注册 `IArenaBountyService`(单例)、`IArenaRevealService`(Scoped)、`IArenaAccusationService` 等（Program.cs:74-121）。

## 六、前端基础设施

- 路由：`router/index.ts`，`/moderation` 布局 + audit/reports/tags/agents 子路由（`meta:{requiresAuth,requiresModerate}`）；已支持 `requiresAdmin`（:168）；`admin/tags` 已重定向到 moderation-tags。
- 治理布局 `views/moderation/ModerationLayout.vue` + `components/moderation/ModerationHubTabs.vue`（审计/举报/标签/智能体四 tab，`v-if="canModerate"` 控智能体 tab）→ 提供可复用的 IA 与菜单范式。
- 令牌唯一源 `styles/forum-tokens.css`（`--color-primary:#1677ff`、`--bg-main`、`--space-*` 8px 网格、`.forum-card`）；纪律 `.cursor/rules/forum-ui.mdc`（禁硬编码色、两行截断、禁 `:deep`）。
- API 客户端：`api/clients.ts:107-112` 有 `reportApi` 实例（base `/api`），但**无业务方法**；需新建 `api/dashboard.ts`。

## 七、关键约束（影响设计，非可选）

- **生产环境 `accountType` claim 从不签发**（`AuthController.cs:687-689` 注明）→ 仪表盘「AI vs 人类」**只能靠 `AuthorSubId` join `agent_personas`（AgentUserId）判定**，不能读 JWT claim。
- **同库跨服务读取**：仪表盘数据分属 Model.Api / Accounting.Api 写入，但**同在 `jissweb_model` 单库**（compose 已确认 Accounting.Api 的 `Mongo__DatabaseName=jissweb_model`）。Report.Api 只需连 `jissweb_model` 一处只读聚合，**无需跨多库**。Customer=`jissweb_customer`、User=`jissweb_user`、Report=`jissweb_report` 各自独立，与本仪表盘无关。

## 八、侦察结论

1. 鉴权、前端 IA/令牌、AIQI/净流量/积分/揭露/悬赏等多数列表结果已落地 → 仪表盘主要是「只读聚合 + 组装 ViewModel + 渲染」，非重算法。
2. 真正需新建的聚合：Top 侦探、周活人类、发帖 AI vs 人类拆分、揭露倒计时、agent 异常派生、LLM 可用性快照。
3. 最大架构决策 = 端点宿主（Report.Api vs Model.Api，已裁定 Report.Api）；数据约束 = AI vs 人类 join；最大缺口 = 无结构化告警表（MVP 可接受派生）。
4. **修正**：仪表盘数据虽分属 Model/Accounting 两服务，但同处 `jissweb_model` 单库 → Report.Api 只需连一处，无跨库复杂度。
