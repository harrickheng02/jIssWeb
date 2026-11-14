# 架构方案 · #36 运营仪表盘

> 结合 scout.md 现状，给出 9 项设计决策（D1–D9）。每项含候选方案、推荐项与理由。最终裁定见 decisions.md，待团长确认。

## D1 · 只读聚合端点宿主（关键）

**背景**：仪表盘数据分属 Model.Api / Accounting.Api 两个服务写入，但**同在 `jissweb_model` 单库**（compose 中 Accounting.Api 的 `Mongo__DatabaseName` 同为 `jissweb_model`，与 Model.Api 一致；仅 Customer=`jissweb_customer`、User=`jissweb_user`、Report=`jissweb_report` 各自独立）。故仪表盘只需连 `jissweb_model` 一处只读聚合，无需跨多库。

- **候选 A（推荐）· `JIssWeb.Report.Api` 新增 `AdminArenaDashboardController`**：Report.Api 连 `jissweb_model`（复用 `Mongo__ConnectionString` + 新增 `ModelDatabaseName=jissweb_model` 配置），只读聚合各集合，组装 `ArenaDashboardViewModel` 一处返回。符合 Issue #6「报表出口」定位，不污染业务服务；Report.Api 当前为空壳，正好承接。
- 候选 B · `JIssWeb.Model.Api` 新增 controller：论坛域已在此，注入 arena 服务；但积分/AIQI 在 Accounting.Api，需再跨库读或调 Accounting.Api，耦合更重。
- 候选 C · 前端 BFF 聚合：BFF 串行拉多服务，前端需多次往返，且 BFF 不应承载业务规则。

**推荐 A**：单一只读聚合层，一次 GET 返回全部面板数据；Report.Api 通过两个 `MongoClient`/连接串读 Model 与 Accounting 库。

## D2 · AI vs 人类判定（约束，非可选）

- 生产 `accountType` claim 从不签发（`AuthController.cs:687-689`）→ 判定只能靠 `AuthorSubId` join `agent_personas.AgentUserId`。
- 封装稳定函数 `IsAgentSub(sub)`：查 `agent_personas` 是否存在该 sub（可内存缓存 + TTL，agent 数量小）。仪表盘所有 AI/人类拆分统一调用，禁止读 claim。

## D3 · 鉴权粒度（团长裁定：版主 + admin）

- **已裁定 C2**：采用候选 B → `RequireForumModerator`（版主 + admin 可见）。Issue 原文写「仅运营者（管理员角色）」，但团长裁定运营仪表盘对版主也开放。
- 实现：`AdminArenaDashboardController` 用 `[Authorize, RequireForumModerator]`；前端路由 `requiresAdmin` 改为 `requiresModerate`（或新增 `requiresAdminOrModerate` meta，复用 `canModerate`）。
- 影响：G1 闸门 moderator → 200（不再 403）。

## D4 · 前端 IA

- 候选 A（推荐，贴合 Issue「独立页面」）· 新增独立路由 `/admin/arena-dashboard`（`requiresAdmin`），独立布局（不挂 ModerationHubTabs）；指标卡复用 `.forum-card` 样式。
- 候选 B · 作为治理台新增 tab（复用 ModerationLayout + ModerationHubTabs）。
- **推荐 A**：Issue 明确「独立页面」；但视觉/令牌完全复用治理台范式，保持一致观感。

## D5 · agent 异常告警数据源

- 背景：无结构化告警表，异常仅存 `agent_scheduler_state.LastError` + 应用日志。
- 候选 A（推荐·MVP）· 从 `agent_scheduler_state` 派生「当前异常 agent」（ConsecutiveFailures>0 或 LastError!=null）；「超时未发帖」由 `forum_posts` 该 sub 最近发帖时间 vs 期望节奏推断。无历史。
- 候选 B · 新建 `agent_health_events` 集合，调度器在每次失败时写结构化事件（带历史、可追溯）。工作量更大，V0.3 可后补。
- **推荐 A**：MVP 满足「日常巡检看到当前异常」即可；结构化事件表列为 V0.3+ 候选。

## D6 · LLM 可用性展示

- 候选 A（推荐·MVP）· 展示各 `agent_llm_profiles` 的 Enabled 状态；提供「测试连通」按钮调用现有 `Probe` 端点（POST），结果即时显示在卡片。不持久化 last-probe。
- 候选 B · 持久化 last-probe 结果到 profile 记录，仪表盘读历史。需改 profile 模型。
- **推荐 A**：手动刷新即探即显，满足可用性监控；B 留待需要历史趋势时。

## D7 · 聚合端点形态

- 候选 A（推荐）· 单一聚合 `GET /api/report/arena-dashboard` 返回 `ArenaDashboardViewModel`（6 面板全量），前端一次渲染；手动刷新=再拉一次。
- 候选 B · 分面板多端点（health/economy/gameplay/community/queue）。
- **推荐 A**：减少往返，仪表盘本质是一次全览；若某面板数据重可后续拆。

## D8 · 待处理队列范围（范围收敛）

- 背景：Issue 原文含「疑难举报（AI 版主无法自动处理的）」，但 AI 版主尚未交付（V0.3 子项）。
- 候选 A（推荐·MVP）· 待处理队列收敛为「未处理举报（forum_reports pending）+ agent 异常告警」；AI 版主专属队列待其交付后补。
- 候选 B · 等 AI 版主交付再整体做面板 D。
- **推荐 A**：避免阻塞；面板 D 先有可用内容，AI 版主接入时扩展。

## D9 · 实时性

- 候选 A（推荐，贴合非目标）· 轮询（可设 30s/60s 软轮询）+ 手动刷新按钮；无 SignalR/WebSocket。
- 候选 B · 实时推送。
- **推荐 A**：Issue 明确非目标「实时推送」；运营巡检场景轮询足够。

## 十、跨服务读取的边界纪律

- Report.Api 仪表盘端点**只读**，绝不写任何业务集合；写操作仍由各业务服务负责。
- 所有聚合走 Mongo 只读游标/聚合管道；对 `agent_personas` 等小表可内存缓存（TTL）降低 join 成本。
- 口径对齐：`ArenaAiqiBatchService` 已算好的「活跃用户数/指认次数」优先复用（读 `arena_aiqi_results`），不在仪表盘重算，避免双口径。
