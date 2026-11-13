# Design · Issue #34 悬赏累积与图灵奖章事件

> 依据：`explore/decisions.md`（两轮 7 项裁定，全部已确认）、`explore/architect.md` D1–D12、`explore/pm.md`。
> 本文为 propose 阶段的技术定稿；explore 文档保留为调研快照。

## 1. 总体结构与依赖方向

```
#34 arena-bounty-medal（Model.Api）
   ├─ 读：agent_personas.CreatedAtUtc / State（只读，绝不回写）
   ├─ 挂：ArenaRevealService.RevealWeekAsync（TryClaim 成功后、prepare 之前）
   ├─ 写：arena_bounty_states / arena_bounty_runs / arena_bounty_medals / arena_hunter_badges
   ├─ 写：arena_points_events 的 delta（复用 #33 唯一写入点）
   ├─ 写：forum_announcements（独立聚合公告）
   └─ 消费方 Accounting.Api：零代码改动，按既有 _id 游标自动入账 ev.Delta
```

铁律：Model.Api SHALL NOT import Accounting.Api、SHALL NOT 读写 `user_points_ledger`；依赖方向保持 `#35 → #33 → #34` 单向。不新增 HostedService（#33 规范禁止复用 per-agent 引擎，且独立周服务会造成双领取）。

## 2. 数据模型（四张独立集合）

| 集合 | 唯一索引 | 关键字段 | 用途 |
|---|---|---|---|
| `arena_bounty_states` | `AgentUserId` | `SurvivedWeeks / CurrentBounty / BountyCap / IsLegendaryPrey / SurvivorMarkedAtUtc / LastComputedWeekKey / MedalAwardedWeekKey / MedalAwardedAtUtc / UpdatedAtUtc` | 每账号当前悬赏与标记，供 #36/#37/#38 消费 |
| `arena_bounty_runs` | `{AgentUserId, WeekKey}` | `WeekKey / ComputedAtUtc` | 按账号 × 周的计算幂等权 |
| `arena_bounty_medals` | `WeekKey` | `WeekKey / MaxBounty / LegendaryCount / AnnouncementId / CreatedAtUtc` | 奖章事件与聚合公告幂等记录 |
| `arena_hunter_badges` | `UserId` | `BadgeId / UserId / TargetWeekKey / AwardedAtUtc / BountyPoints` | 图灵猎手永久成就 |

**SHALL NOT** 扩展 `AgentPersonaRecord` / `AgentPersonaState` / `ForumAnnouncementRecord` / `ArenaPointsEventRecord` 的字段。理由：
- `AgentPersonaState.IsValid` 白名单仅 `active/eliminated/archived`，且 `AgentSchedulerEngine` 只挑 `Active` 调度；新增状态会连带停止自动发帖。
- `ForumAnnouncementRecord` 加门禁字段会与 `arena-weekly-reveal` 的「唯一对外可见性门禁」冲突。
- `ArenaPointsEventRecord` 与消费方镜像均无 `[BsonIgnoreExtraElements]` / `[BsonElement]` / 全局 Convention，新增字段会让 #35 反序列化抛异常。

## 3. 悬赏推导（P0：纯函数，绝不 ×2 累加）

```
survivedWeeks = floor((weekEndUtc - persona.CreatedAtUtc) / 7d)
bounty        = min(BasePoints × 2^(survivedWeeks-1), CapPoints)   // survivedWeeks >= 1
```

- 输入仅 `CreatedAtUtc` 与 `weekKey`；输出幂等；每周用 `Set` 而非 `Inc` 写回。
- `ArenaRevealBackgroundService.CatchUpAsync` 启动补跑最多 12 个 `weekKey`，若实现成 `bounty *= 2`，一次重启放大 4096 倍且不收敛，故此项为 P0。
- `survivedWeeks == 0` 或 `CreatedAtUtc` 缺失 / `DateTime.MinValue`：跳过计算并记日志，SHALL NOT 抛异常、SHALL NOT 阻断结算。
- 只处理 `State == active` 且本 `weekKey` 未被揭露的账号；`eliminated` 停止累积。
- `SurvivalDays` 是运营手工字段，SHALL NOT 作为计时源。

## 4. 周键口径

三套周键并存，唯一权威为 **#33 `ArenaWeekKeys`**（`Asia/Shanghai` 周一起算），与结算批次同界：

| 来源 | 口径 | 用途 |
|---|---|---|
| #33 `ArenaWeekKeys` | Asia/Shanghai 周一 | **`weekKey` / `WeekId` / `eventId` / 悬赏计算的唯一权威** |
| #35 `ArenaAiqiBatchService.StartOfWeek` | UTC 周日起算 | 仅 AIQI 周分桶（全库无 `ArenaMetricWeek` 类） |
| 前端 `arenaAnalytics.ts:getArenaWeekKey` | 浏览器本地时区 | 仅埋点聚合计数 |

新增埋点若带 `weekKey`，SHALL 由后端下发或显式按 +8 计算，SHALL NOT 用浏览器本地时区做业务判定。`eventId = ArenaEventId.Derive(weekKey, userId, targetUserId, type)` 不变。

## 5. 挂接与幂等

在 `RevealWeekAsync` 内 `TryClaimWeekAsync` 成功后、`PrepareWeekAsync` 之前执行当周悬赏计算：

1. 对每账号原子 `Insert` `arena_bounty_runs` 领取本周计算权，11000 静默跳过（因 `CatchUpAsync` 会跑 12 个**不同** weekKey，`TryClaimWeekAsync` 只保证单 weekKey 一次领取）。
2. 纯函数计算 → `Set` 回 `arena_bounty_states`。
3. 计算失败 SHALL NOT 阻断当周揭露：降级为「本周不更新悬赏」，记日志，结算继续；此时积分缺省恰好 50。
4. `CommitWeekAsync` 读悬赏值发积分（commit 在 prepare 之后，天然满足「揭露前完成计算」）。

**#33 in-progress 缺口（已登记，零改动 #33）**：`RevealWeekAsync` 对 `settling/settled/revealing` 返回 `Skipped` 且全仓无接管路径，该周永不重算。本期解法：
- 数值类靠纯函数下一周自愈，无累积误差。
- 一次性事件（奖章公告、幸存者标记）触发条件 SHALL 为 **`survivedWeeks >= MedalWeeks` 且 `MedalAwardedAtUtc == null`**（下界 + 未发放标记），而非「恰好等于 4」；配合 `arena_bounty_runs` 与固定 `Id` 幂等，保证全网每账号恰好发一次、补跑不漏发。
- 缺口本身作为独立 Issue 候选登记，**不在 #34 修复**（改状态机超范围）。

## 6. 积分经济（N1 / N2 定稿）

- `type` 枚举保持 `accusation_hit | accusation_miss`；**零新增字段**；事件只追加、无状态字段；`eventId` 派生与唯一索引 `{WeekId,UserId,TargetUserId,Type}` 不变。
- `ArenaRevealService.cs` 的 `Delta = 50` 改为按目标当前 `weekKey` 悬赏值取；**缺省路径（灰度关闭 / 状态缺失 / 计算失败）恰好 50**。
- **每人全额**：`foreach (var acc in accusations)` 对每位成功指认者各写一条 hit，`Delta` 恰为当前悬赏值。SHALL NOT 按人数分摊、SHALL NOT 除以 N、SHALL NOT 只奖励前 K 人——任何缩放或名单截断都违反 #33 verdict 表「其全部 counted 指认者 SHALL 获得 `accusation_hit`」与 `eventId` 幂等语义。
- **双层封顶**：① 悬赏值硬上限 `CapPoints`（默认 400，可配）；② 单周结算总投放上限 `MaxPayoutPerWeek`（默认 `CapPoints × RevealThreshold`，可配）。超额时 SHALL 记录运营告警、SHALL NOT 削减已判定金额；由运营调低 `CapPoints` 或 `Enabled=false` 干预。悬赏导致净流量上升属预期，`InflationAlert` 语义在 G4 确认可接受即不视为回归。
- **AIQI 口径保护**：`ArenaAiqiBatchService` 的 `weekEvents` 只收 `Hit|Miss` 且把 `weekEvents.Count` 计入 `totalAccusations`；因不新增 type，`hits / misses / totalAccusations / DetectionSuccessRate / AIQI` 与 +50 基线样本完全一致（仅净流量金额变化）。Accounting.Api `ApplyBalanceAsync` 直接 `$add ev.Delta` 且不校验金额/type，**零代码改动**。
- 前端 `PointsPanel.vue` 的 `typeLabel` 已映射 `accusation_hit`、`item.delta` 本就动态渲染，无需改动。
- **编译级红线**：若给 `ArenaRevealService` 构造函数新增 `IOptions<ArenaBountyOptions>` / `IArenaBountyService`，`ArenaRevealPipelineTests.cs:32-38` 与 `ArenaRevealServiceTests.cs:33-39` 两个 `CreateService()` 均为 6 参数**位置传参**，会直接编译失败，SHALL 同步补参；SHALL NOT 用服务定位器绕开签名。

## 7. 聚合公告（C1 / C2 定稿）

- 文案只含聚合信息（例「本周出现传说级猎物，当前最高悬赏 400 分」），SHALL NOT 含账号名、`targetUserId`、其哈希、存活天数与账号的对应关系、任何可枚举单账号的线索。
- **零字段扩展、零门禁字段**：`ForumAnnouncementRecord` 保持 `Id/Title/Summary/LinkUrl/PublishedAtUtc/Pinned`；不引入 `BatchId/ReleasedAtUtc/AnnouncedAtUtc/IsPublic`。公告属**悬赏域独立事件**，不是结算批次新增数据，不受 `reveal_jobs.state=completed` 管辖（该规范自身已限定「门禁仅作用于本批次新增数据」），故不引入第二门禁。
- 写入方式：后台服务直接 `InsertOneAsync`，固定 `Id = "arena-bounty-medal-" + weekKey` + 11000 静默跳过；**不新增 HTTP 写端点**，`ForumAnnouncementsController` 保持只有 `HttpGet List`、无需加过滤；`forum-content-api/spec.md:286` 的「read-only」修饰端点而非集合，故无需 MODIFIED。
- SHALL NOT 设 `linkUrl`（`/arena/hall-of-turing` 属 #37，路由尚不存在，`HomeView.vue` 走静态标题分支无死链）。
- SHALL NOT 复用 `forum_in_app_notifications`（per-recipient）。个人奖励通知沿用 #33 `ArenaRevealHit` 通道，金额改为悬赏值、机制不变。
- BFF `/api/bff/forum-init` 为 HttpClient 转发、不直连 Mongo，读路径只需考虑 `GET /api/forum/announcements` 一处。

## 8. 徽章与幸存者标记

- 徽章在 `CommitWeekAsync` 成功指认传说级猎物时授予 `arena_hunter_badges`，唯一键 11000 幂等；前端在 `PersonalCenterLayout.vue` 的 `el-menu` 挂入口 + 只读视图 + `useHunterBadge` composable，遵守 `.cursor/rules/forum-ui.mdc`。
- **只读数据源（propose 收口补记）**：新增 `GET /api/arena/hunter-badge/me`（Model.Api、`[Authorize]`、按 JWT `sub` 过滤），只返回当前认证用户的徽章记录，SHALL NOT 返回其他用户数据或目标账号标识。这是徽章展示的**唯一数据源**；它不属公告写入端点（公告仍由后台服务直接写 `forum_announcements`），故 `proposal.md` 的「不新增 HTTP 端点」已收紧为「不新增公告写入端点」。
- 埋点经 `arenaAnalytics.ts:assertAllowedAnalyticsKeys` 白名单，仅 `ts / accuserIdHash / weekKey / 聚合计数`。
- 幸存者：`SurvivorMarkedAtUtc` 在「存活满 `SurvivorWeeks` 且仍 active」时写入；SHALL NOT 触发强制揭露、SHALL NOT 写 `agent_experiences`、SHALL NOT 建名人堂页面、SHALL NOT 改 `persona.State`。

## 9. 设计铁律（C4/C5）

AIQI 对 #34 不可消费（只写无对外读取端、周级全局单值无账号维度、`0.8` 比较全仓不存在）。因此：
- 悬赏累积 SHALL NOT 读取 AIQI 作为触发门槛；触发源锚定按账号存活周数。
- 系统 SHALL NOT 因 AIQI 高而降低提示词质量、暂停账号、提前揭露或点名；写成约束声明 + 断言测试。
- 不新增 per-agent AIQI、不新增跨服务读取链路（否则反转 `#35 → #33` 单向依赖）。若后续坚持按账号 AIQI，属 #35 新增工作量，须另行立项。

## 10. 灰度、上线与回滚

`ArenaBountyOptions`：`Enabled`（**默认 false**）、`BasePoints=50`、`CapPoints=400`、`MedalWeeks=4`、`SurvivorWeeks=8`、`AnnouncementEnabled=true`、`MaxPayoutPerWeek`（默认 `CapPoints × RevealThreshold`）。全部可配置，SHALL NOT 硬编码。

- 关闭时 SHALL 零写入 `arena_bounty_states`、零公告、积分回落恰好 +50（#33 行为零变化）。
- 上线顺序：合入默认关 → 测试环境快进 4 个 `weekKey` 验证 50/100/200/400 → 生产先开数据、再开公告 → 连续观察 2 个结算周期的 AIQI / `InflationAlert` / WAD。
- 一级回滚 `Enabled=false`（无需回滚代码）；二级 `git revert`（四张新集合独立，删除不影响既有数据）；SHALL NOT 回滚 `arena_points_events` 已写入事件。

## 11. 契约改动面

| 规范 | 动作 | 内容 |
|---|---|---|
| `arena-bounty-medal` | ADDED | 悬赏累积 / 聚合公告 / 徽章与积分 / 幸存者标记 / 设计铁律 五条 Requirement |
| `arena-points-events` | MODIFIED | 3 条 Requirement 整段重写：放开 `delta`、零新增 type/字段、补 AIQI 口径保护；逐条改写 `:13/:35/:51/:53/:58/:71` |
| `arena-weekly-reveal` | MODIFIED | 3 条 Requirement 整段重写：挂接悬赏计算、`:29` 门禁作用域澄清、逐条改写 `:35/:49/:64/:80`；三分支状态机不变 |
| `arena-silent-accusation` / `forum-content-api` / `agent-account-protocol` / `agent-memory` / `arena-aiqi-metric` / `arena-skill-shop` | 不改动 | 见 `explore/decisions.md` 连带结论 1/5 与 D11 |

## 12. 已知技术债（登记，不在 #34 修复）

① #33 `reveal_jobs` in-progress 无接管路径；② 跨服务 `OccurredAt` vs `OccurredAtUtc` 字段命名不一致且无 BSON 映射；③ `ArenaRevealService` 内 N+1 查询；④ `arena_pending` 在 `GetById` 未显式过滤；⑤ AIQI `SampleSufficient` 生产恒 true、周日 00:00 规范与 6h 轮询实现偏差；⑥ 三套周键并存。

## 13. 测试与闸门

新增测试类前缀 `ArenaBounty*`，自动落入 CI `--filter "FullyQualifiedName~Arena|FullyQualifiedName~Agent"`（**无需改 ci.yml**）。Mongo2Go 自包含 + `WebApplicationFactory` + stub 范式。详见 `explore/test-checklist.md`（A–F 全表）与 `explore/launch-gates.md`（G1–G5、H1–H3）。
