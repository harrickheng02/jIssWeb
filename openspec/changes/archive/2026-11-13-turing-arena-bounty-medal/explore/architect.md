# Architect 技术方案 · Issue #34 悬赏累积与图灵奖章事件

> 依据：`decisions.md`（两轮共 7 项裁定，**全部已确认**）+ `scout.md`（现状）+ 四份并行调研报告
> 状态：**已批准**，作为 propose 的 `design.md` / delta spec 的输入；7 项裁定全部落定，无待定项。

## D1 · 悬赏状态落独立集合，不改 persona 契约

新增 `arena_bounty_states`（`AgentUserId` 唯一索引），字段：

```
AgentUserId / SurvivedWeeks / CurrentBounty / BountyCap
/ MedalAwardedWeekKey / MedalAwardedAtUtc
/ IsLegendaryPrey / SurvivorMarkedAtUtc / LastComputedWeekKey / UpdatedAtUtc
```

理由：`AgentPersonaRecord` 的 `SurvivalDays` 是运营手工字段（`AdminAgentPersonasController.cs:162`），不能作为计时源；把状态塞进 persona 会污染 #30 账号协议契约。更硬的证据是：

- `AdminAgentPersonasController.cs:148-152` 用 `AgentPersonaState.IsValid(state)` 白名单校验管理端 PUT，非法返回 `INVALID_STATE`；`AgentPersonaRecord.cs:46-50` 的 `IsValid` **只接受 `active/eliminated/archived`**。新增「legendary / survivor」状态会让运营端改状态被拒。
- `AgentSchedulerEngine.cs:57,66` 只挑 `State == Active` 的 persona 做 per-agent 调度 → 新增非 active 状态会**连带停止自动发帖**（与「高 AIQI 只升不降、继续参与」的设计原则直接矛盾）。

独立集合同时便于 #36/#37/#38 消费，且 `AgentPersonaRecord` 有 `[BsonIgnoreExtraElements]` 可平滑演进。

**SHALL NOT** 读写 `persona.SurvivalDays`；**SHALL NOT** 扩展 `AgentPersonaState`；**SHALL NOT** 改变 `persona.State`（幸存者标记只落 `arena_bounty_states`）。

## D2 · 悬赏值用纯函数推导，绝不「×2 累加」（P0）

```
survivedWeeks = floor((weekEndUtc - persona.CreatedAtUtc) / 7d)   // weekEndUtc 由 weekKey 推导
bounty        = min(BasePoints × 2^(survivedWeeks-1), CapPoints)   // survivedWeeks >= 1
```

只用 `CreatedAtUtc` 与 `weekKey` 两个输入做**幂等纯计算**，每周批次 `Set`（非 `Inc`）写入 `CurrentBounty` / `SurvivedWeeks` / `LastComputedWeekKey`。

**为什么这是 P0**：`ArenaRevealBackgroundService.CatchUpAsync:87-97` 启动会补跑最多 **12 周** `weekKey`；若实现成 `bounty *= 2`，一次重启就会把悬赏错误放大 4096 倍，且重跑不收敛。纯函数 + `Set` + `LastComputedWeekKey` 幂等，可安全补跑。

## D3 · 周键口径统一到 #33（跨服务偏差登记）

- **三套周键并存（四份调研交叉确认）**，须明确唯一权威口径：
  1. **#33 Model.Api `ArenaWeekKeys`**：`Asia/Shanghai` 周一起算（`Tz(8 hours)` + `DayOfWeek.Monday`），是 `weekKey` / `WeekId` / `eventId` / `reveal_jobs` 的事实标准。
  2. **#35 Accounting.Api `ArenaAiqiBatchService.StartOfWeek`**：`dt.Date.AddDays(-(int)dt.DayOfWeek)` 按 **UTC 周日**起算，`WeekKeyOf` 输出 `yyyy-MM-dd`，仅用于 AIQI 周分桶。**注意：全库不存在 `ArenaMetricWeek` 这个类**（早前草稿误记，已更正）。
  3. **前端 `arenaAnalytics.ts:64` `getArenaWeekKey`**：浏览器本地时区 `getDay()` 算周一，仅用于埋点聚合计数，不参与任何业务判定。
- 悬赏是「图灵场游戏周期」，**统一采用 #33 的 `ArenaWeekKeys`**，与结算批次同界，避免 7 小时错位导致存活周数算错。前端 SHALL 由后端下发 `weekKey`（或按 `Asia/Shanghai` 显式计算），SHALL NOT 用浏览器本地时区周键做业务判定；本期新增埋点若带 `weekKey`，必须走这一口径。
- `arena_points_events` 的 `WeekId` 继续沿用 #33 的 `weekKey`，`eventId = ArenaEventId.Derive(weekKey, userId, targetUserId, type)` 不变。
- 既有偏差（跨服务 `OccurredAt` vs `OccurredAtUtc` 字段命名、`ArenaRevealService` 内 N+1 查询、`arena_pending` 在 `GetById` 未显式过滤）**登记为已知技术债，不在 #34 修复**，避免范围膨胀。

## D4 · 挂接点：结算批次 prepare 之前，独立幂等

在 `ArenaRevealService.RevealWeekAsync`（`:80`）内，`TryClaimWeekAsync` 成功后、`PrepareWeekAsync`（`:125`）之前调用 `IArenaBountyService.ComputeWeekAsync(weekKey)`：

- 复用 `reveal_jobs` 的 `weekKey` 原子领取，**不新建 HostedService**（#33 规范明确 SHALL NOT 复用 per-agent 引擎，且新增独立周服务会造成双领取）。
- 计算失败 SHALL NOT 阻断当周揭露；降级为「本周不更新悬赏」，记录日志，结算继续。
- 计算结果供 `CommitWeekAsync:211-228` 读取悬赏值发积分（commit 在 prepare 之后，天然满足「揭露前完成计算」）。
- 只处理 `persona.State == active` 且本 `weekKey` 未被揭露的账号；已 `eliminated` 的 SHALL NOT 继续累积。
- **按账号 × weekKey 的独立幂等集合 `arena_bounty_runs`**（唯一索引 `{AgentUserId, WeekKey}`）：`ComputeWeekAsync` 先原子 Insert 领到本周计算权，11000 静默跳过。理由：`TryClaimWeekAsync` 只保证**单个 weekKey** 一次领取，但 `CatchUpAsync:90-95` 会跑 12 个**不同** weekKey；且悬赏计算与结算是两个独立关注点，不应把「是否算过悬赏」绑定在 `reveal_jobs` 的状态上。
- **in-progress 缺口（#33 既有实现缺陷，已登记）**：`RevealWeekAsync:85-90` 对 `settling/settled/revealing` 状态返回 `NotOwned → Skipped`，全仓无 `ResetStaleJobAsync` 之类的接管路径，卡住那一周**永不重算**。对 #34 的影响分两类：
  - **数值类**（`SurvivedWeeks`/`CurrentBounty`）：纯函数 `Set`，下一周按 `CreatedAtUtc` 重算即自愈，**无累积误差**。
  - **按周一次性事件**（奖章公告、幸存者标记）：若「恰好第 4 周」被 Skipped，会**永久漏发**（下周 `survivedWeeks=5` 时若仅判断「等于 MedalWeeks」则不触发）。
  - **本期解法（第二轮 N3 已裁定）**：一次性事件的触发条件 SHALL 使用 **`survivedWeeks >= MedalWeeks` 且 `MedalAwardedAtUtc == null`**（下界 + 未发放标记），而非「恰好等于 4」；公告幂等键锚定 `weekKey` 但条件为「本周存在首次达标账号」，配合 `MedalAwardedAtUtc` 保证全网每账号恰好发一次。补跑时不因周次跳过而漏发。
  - **范围裁剪已裁定**：采用 ①「仅登记该缺口、用上述下界条件在本期自愈、零改动 #33」；**不采纳** ②「把补 in-progress 续跑列为 #34 前置 P0」（会改 `ArenaRevealService` 状态机，超出 #34 范围）。缺口本身作为独立 Issue 候选登记。

## D5 · 积分事件：放开 delta，**不新增 type**（P0，AIQI 口径保护）

团长首轮 C3 裁定为「扩展事件契约」，第二轮 N2 已确认收紧形态。调研实证 `ArenaAiqiBatchService.cs:85-87` 的 `weekEvents` 只收 `Hit|Miss` 两类，且 `:87` 直接把 `weekEvents.Count` 计入 `totalAccusations`（AIQI 公式的分母之一）→ 若新增 `bounty_hit` 类型，**该笔指认会从分母消失、检测成功率被高估、AIQI 虚高**，同时 `arena_skill_shop` 的 `typeLabel` 会把新类型显示为原始英文 key。因此最小且正确的扩展形态是：

- MODIFIED `arena-points-events`：把 `delta = +50 | -5` 冻结条款改为 `delta` 为整型，其中 `accusation_hit` 的绝对值 SHALL 等于该目标在当前 `weekKey` 的悬赏值（默认 50，上限可配置）；`accusation_miss` 固定 -5 不变。
- `type` 枚举**保持** `accusation_hit | accusation_miss` 不变；`eventId` 派生不变；事件只追加、无状态字段的约束不变。
- **SHALL NOT 给 `arena_points_events` 新增任何字段**（含原稿设想的 `bountyPoints`）。实证：生产方 `ArenaPointsEventRecord.cs:10-22` 与消费方 `ArenaSourceEventRecord.cs:8-20` **均无 `[BsonIgnoreExtraElements]`、无 `[BsonElement]`、无全局 ConventionRegistry**；消费方按 `OccurredAtUtc` 过滤 `arena_points_events`，生产方写的是 `OccurredAt`（既有缺陷）。新增字段会让 #35 消费方反序列化遇到未映射元素而抛异常。悬赏值只通过 `delta` 表达，审计可直接在事件文档读 `delta`，无需额外字段。
- `ArenaRevealService.cs:214-224` 的 `Delta = 50` 改为从 `IArenaBountyService` 读取该目标悬赏值；非传说级目标仍为 50。**复用全仓唯一写入点**（`AppendEventAsync` 仅 `:214` hit / `:248` miss 两处），不新增事件写入方、不新增 Accounting.Api 发放端点。
- **多指认者口径（第二轮 N1 已裁定「每人全额 + 封顶」）**：`foreach (var acc in accusations)` 对每位成功指认者各写一条 hit，`Delta` 恰为当前悬赏值，SHALL NOT 按人数分摊 / 除以 N / 只奖励前 K 人（任何缩放或名单截断都会违反 #33 verdict 表「其全部 counted 指认者 SHALL 获得 `accusation_hit`」）。双层封顶：① 悬赏值硬上限 `CapPoints`（默认 400）；② 单周结算总投放上限 `MaxPayoutPerWeek`（默认 `CapPoints × RevealThreshold`，可配），**超额时 SHALL 记录运营告警、SHALL NOT 削减已判定金额**，由运营调低 `CapPoints` 或 `Enabled=false` 干预。悬赏导致净流量上升属预期，`InflationAlert` 语义在 G4 确认可接受即不视为回归。
- #35 消费者 `ApplyBalanceAsync`（`:114-149`）直接把 `ev.Delta` 塞进 `$add`（`:127`），不校验金额与 type（`:101` 的 Miss 仅用于免死护盾），同时写 ledger 与 transactions → **账本所有权仍留 #35，Accounting.Api 一行代码都不用改**；依赖方向保持 `#35 → #33` 单向，#34 不写 `user_points_ledger`。
- 前端 `PointsPanel.vue:9-14` 的 `typeLabel` 已映射 `accusation_hit`，**无需改动**；若当初新增 type 则会回显原始英文 key。
- **编译级改动面（propose 收口补记，P0）**：若给 `ArenaRevealService` 构造函数新增 `IOptions<ArenaBountyOptions>` / `IArenaBountyService`，`ArenaRevealPipelineTests.cs:32-38` 与 `ArenaRevealServiceTests.cs:33-39` 两个 `CreateService()` 均为**位置传参**（当前 6 参数），会直接编译失败。tasks SHALL 明确同步这两个 helper，SHALL NOT 用服务定位器绕开签名（反模式）。

> 这是对 C3 裁定**实现形态**的收紧（仍属「扩展事件契约」，但「不新增 type、不新增字段、只放开 delta 语义」）。三条实证理由：AIQI 分母污染、BSON 反序列化风险、前端标签回显。**已由团长第二轮 N2 确认，直接写入 delta spec，无需再请示。**

## D6 · 全站公告：聚合口径 + **零门禁字段**（最终形态）

- 公告文案 SHALL 只含聚合信息（例：「本周出现传说级猎物，当前最高悬赏 400 分」），**SHALL NOT** 含账号名、`targetUserId`、哈希、存活天数与账号的对应关系、任何可枚举单账号的线索。
- **SHALL NOT 引入任何对外可见性门禁字段**（`BatchId` / `ReleasedAtUtc` / `AnnouncedAtUtc` / `IsPublic` 均不新增），`ForumAnnouncementRecord` **零字段扩展**。理由：C1 裁定不点名后，公告内容零身份信息、不存在需要隐藏的中间态；若引入独立门禁，会与 `arena-weekly-reveal/spec.md` 的「**唯一**对外可见性门禁」表述正面冲突，凭空多出第二个门禁。
- 公告是**悬赏域独立事件**，不是结算批次新增数据，不受 `reveal_jobs.state=completed` 管辖（该规范自身亦限定「门禁仅作用于本批次新增数据」）。新增 Requirement SHALL 显式声明此点，消除字面歧义；`arena-weekly-reveal` 门禁条款的「唯一」字眼**不改**，仅在 `:29` 补一句作用域澄清（见 D11）。
- 幂等：`Id = "arena-bounty-medal-" + weekKey` 固定派生，`InsertOneAsync` 遇 11000 静默跳过；**不复用结算计划、不挂 `ReleaseWeekAsync`**。
- 新增写入服务（当前 `ForumAnnouncementsController.cs:14-32` 只有 `HttpGet List`，无任何写端点）；`ForumAnnouncementsController.List` **无需加过滤**（无状态切换，`Cache-Control: public, max-age=60` 只影响新公告最多 60 秒后刷新可见，且不涉及身份数据）。
- BFF `/api/bff/forum-init` 为 HttpClient 转发、不直连 Mongo，故读路径过滤只需考虑 `GET /api/forum/announcements` 一处。
- SHALL NOT 复用 `forum_in_app_notifications` 发全站公告（该集合是 per-recipient）。
- **需求变更登记**：`pm-plan.yaml:328` 与 GitHub Issue #34 正文含明文点名文案「[账号名] 已在社区潜伏 N 天…」，按团长 C1 裁定改为聚合口径；归档时需同步改这两处文案，避免 spec 与产品端文案打架。

## D7 · 图灵猎手徽章：独立集合 + 最小前端

- 新增 `arena_hunter_badges`（`UserId` 唯一索引）：`BadgeId / UserId / TargetWeekKey / AwardedAtUtc / BountyPoints`。授予在 `CommitWeekAsync` 成功指认传说级猎物时执行，唯一键 11000 静默跳过保证幂等。
- 前端：`PersonalCenterLayout.vue` 的 `el-menu` 挂一个新入口 + 一个只读视图；新增 `useHunterBadge` composable，遵守 `.cursor/rules/forum-ui.mdc`（forum-tokens 变量、8px 网格、无硬编码色、标题 2 行截断、不用 `:deep` 覆盖 EP）。
- 埋点 SHALL 走 `frontend/src/utils/arenaAnalytics.ts` 的 `assertAllowedAnalyticsKeys` 白名单，只含 `ts / accuserIdHash / weekKey / 聚合计数`。

## D8 · 幸存者标记降级（C2）

`SurvivorMarkedAtUtc` 在「存活满可配置 `SurvivorWeeks` 且仍 active」时写入；**SHALL NOT** 触发强制揭露、SHALL NOT 写 `agent_experiences`、SHALL NOT 建名人堂页面。记录供 #37/#38 消费。

## D9 · 设计铁律的落地形态（C4/C5）

调研实证（四份报告交叉确认）表明 AIQI 对 #34 **根本不可消费**，三重障碍：

1. **只写不读**：`arena_aiqi_results` / `arena_netflow_results` 全仓仅 `ArenaAiqiBatchService.cs:110-126` 写入 + 测试读取；Accounting.Api 的 4 个 Controller（Health/Sample/Points/Skills）**无 AIQI 查询端点**，Report.Api 仍是骨架，前端无仪表盘。
2. **粒度错配**：`ArenaAiqiResultRecord`（`ArenaPointsModels.cs:86-98`）是周级全局单值、无账号维度；PRD:381 的「该智能体近乎通过图灵测试」是按账号语义。
3. **阈值未实现 + 值本身可信度存疑**：`0.8` 比较在全仓不存在；且 `ArenaPointsEventRecord.OccurredAt` 与消费方镜像 `ArenaSourceEventRecord.OccurredAtUtc` 字段名不一致、无 BSON 映射，AIQI 周分桶依赖该字段；`activeUsers` 与 `totalAccusations` 同源导致 `SampleSufficient` 生产恒 true。

若强行消费，只有两条路且都不可取：新增 Accounting 只读端点（引入 Model.Api→Accounting.Api 跨服务调用）、或 Model.Api 直读共享库（**反转 `arena-points-events/spec.md:21` 的单向依赖 `#35 → #33`**）。

因此：

- 铁律 SHALL 写成**约束声明 + 断言测试**：系统 SHALL NOT 因 AIQI 高而降低提示词质量、暂停账号、提前揭露或点名；悬赏累积 SHALL NOT 读取 AIQI 作为触发门槛。
- 触发源锚定**按账号存活周数**（`CreatedAtUtc` + `ArenaWeekKeys`），不依赖 AIQI 读取。
- 不新增 per-agent AIQI、不新增跨服务读取链路。若后续坚持按账号 AIQI，属 #35 新增工作量，须作为 #34 的显式前置依赖另行立项。
- 上述三项 AIQI 既有缺陷（`OccurredAt` 命名、样本判定不可达、周日 00:00 规范与 6h 轮询实现偏差）**登记为已知技术债，不在 #34 修复**。

## D10 · 灰度开关

`ArenaBountyOptions`：`Enabled`（**默认 false**）、`BasePoints=50`、`CapPoints=400`、`MedalWeeks=4`、`SurvivorWeeks`（默认 8，可配置）、`AnnouncementEnabled=true`、`MaxPayoutPerWeek`（默认 `CapPoints × RevealThreshold`）。开关关闭时 SHALL 零写入、零公告、积分回落到 #33 的 +50 基线。

## D11 · 契约改动面（delta spec 目标）

| 规范 | 动作 | 内容 |
|---|---|---|
| `arena-bounty-medal` | **ADDED**（新能力） | 悬赏累积 / 聚合公告 / 徽章与积分 / 幸存者标记 / 设计铁律 五条 Requirement |
| `arena-points-events` | **MODIFIED** | 放开 `delta` 冻结条款为「`type` 不变、`delta` 为按悬赏值取的可变正整型」；**零新增字段**；`eventId` 与「只追加无状态」不变；补 AIQI 口径保护条款；**逐条改写 `:13` 冻结文字与 `:35/:51/:53/:58/:71` 的「+50」文案**（`:35` 为四份调研交叉核对后补入，勿漏） |
| `arena-weekly-reveal` | **MODIFIED** | ① 结算批次挂接悬赏计算（prepare 前）；② 逐条改写 `:35/:49/:64/:80` 的「+50」措辞为「悬赏值」；③ **在 `:29`「唯一对外可见性门禁」Requirement 补一句作用域澄清**（不改「唯一」字眼）：该门禁针对结算批次产出的数据，与 `:60`「门禁仅作用于本批次新增数据」自洽；`arena-bounty-medal` 的聚合公告属独立域事件、零身份信息、写入即可见，不构成第二个门禁；④ 三分支状态机不变 |
| `arena-silent-accusation` | **不改动** | C1 聚合公告零账号标识，隐私红线保持 |
| `forum-content-api` | **不改动** | `:286` 的「read-only」修饰端点而非集合，后台写不违约 |
| `agent-account-protocol` / `agent-memory` / `arena-aiqi-metric` / `arena-skill-shop` | **不改动** | persona 契约、经验集合、AIQI 口径、技能商店均不受影响 |

## D12 · 测试策略

Mongo2Go 自包含集成测试 + WebApplicationFactory，沿用 `ArenaRevealIntegrationFixture` / stub 注入范式；新增测试类前缀 `ArenaBounty*`，自动落入现有 CI 门禁 `--filter "FullyQualifiedName~Arena|FullyQualifiedName~Agent"`（**无需改 ci.yml**）。详见 `test-checklist.md`。

## 裁定状态（原「待团长确认的一处收紧」已解除）

D5 把 C3「新增 bounty 类型」收紧为「不新增 type、只放开 delta」，理由是保护 AIQI 统计口径。**团长已于第二轮 N2 确认该收紧**，据此写入 delta spec。若未来坚持新增独立 type，则必须同步 MODIFIED `arena-aiqi-metric` 并让 `ArenaAiqiBatchService` 把新类型计入 hits/总数 —— 范围会扩到 #35，本期不采纳。
