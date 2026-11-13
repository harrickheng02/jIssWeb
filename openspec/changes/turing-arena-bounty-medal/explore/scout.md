# Scout 现状调研 · Issue #34 悬赏累积与图灵奖章事件

> 阶段：/opsx:explore（只读调研，未做任何实现决策）
> 日期：2026-11-14
> 结论摘要：#34 相关代码**零实现**，且验收标准与 #33 已归档契约、PRD「设计铁律」、相邻 Issue 边界存在 5 处结构性冲突（已于 2026-11-14 由两轮 7 项裁定收口，见 `decisions.md`）。
> 状态更新（2026-11-14）：上述冲突及调研后半程浮现的 3 项新增问题，已由**两轮共 7 项裁定全部收口**（见 `decisions.md`），无待定项；本文件保留为 explore 阶段的只读现状快照，第 5、6 节的「需裁定」表述按当时状态理解。

## 1. 需求来源与边界

| 来源 | 位置 | 关键内容 |
|---|---|---|
| GitHub Issue #34 | `state=OPEN`，`priority=主要`，`module=游戏机制` | 悬赏累积 / 图灵奖章事件 / 图灵猎手徽章 / 赛季结束幸存者 / 设计铁律 |
| pm-plan | `scripts/github-sync/pm-plan.yaml:322-342` | `state: open`、`milestone: 图灵场 V0.2 — 游戏机制 MVP`、OpenSpec change `turing-arena-bounty-medal`（待 propose） |
| PRD | `docs/PRD-turing-arena.md:379-386 / 400-433 / 738-752 / 809-814` | AIQI 区间表、每周再平衡伪码、悬赏翻倍表、图灵奖章事件、赛季制属 V1.0 之后 |

相邻 Issue 边界（避免越界）：

- **#37 图灵名人堂**（V1.0，open）：公开页 `/arena/hall-of-turing`、账号档案页、赛季纪念 LLM 生成。依赖 #34。
- **#38 经验继承**（V1.0，open）：**经验提取器**（LLM 分析获奖账号全量历史，写 `agent_experiences`）、时间衰减、Top-5 注入、世代追踪。依赖 #32 与 #37。
- **#36 运营仪表盘**（V0.3，open）：游戏进展面板含「传说级猎物列表」，依赖 #34 的数据源。
- #34 自述「非目标：赛季制框架（V1.0 后）」，但验收标准第 4 条又要求「赛季结束仍未被发现 → 强制揭露 → 进入图灵名人堂」。

## 2. 代码现状（确证：零实现）

- `backend/src` 与 `frontend/src` 全库检索 `bounty|medal|legendary|传说|悬赏|survivor|幸存者` → **无任何匹配**。
- `AIQI > 0.8` 判定 → **未落地**；唯一 `0.8` 是 `AgentSchedulerServices.cs:35` 的 LLM `temperature`。
- `openspec/specs/` 下 60 份主规范中**无 bounty / medal 能力**；`openspec/changes/` 无活跃 change、无 `turing-arena-bounty-medal` 残留。
- 结论：#34 是**从零新建能力**，不是「接线」；但可复用 #33 的周批次工程资产与 #32 的经验集合。

## 3. 可复用的既有资产

| 资产 | 位置 | 可复用点 / 限制 |
|---|---|---|
| 周批次调度 | `ArenaRevealBackgroundService.cs:17-106` | 周五 22:00 `Asia/Shanghai`、缺 tzdata 降级 +8、启动补跑 12 周、`weekKey` 原子领取。**规范明确 SHALL NOT 复用 per-agent 引擎** |
| 结算两阶段 | `ArenaRevealService.cs:125`（prepare）/ `:166`（commit） | `reveal_jobs.state=completed` + `publishedAtUtc` 是**唯一对外可见性门禁** |
| 奖励写死点 | `ArenaRevealService.cs:211-228` | 每个 hit 指认者硬编码 `Delta = 50`，`eventId = ArenaEventId.Derive(weekKey, userId, targetUserId, type)` |
| 事件唯一索引 | `ArenaMongoSetup.cs:12,75` | `(weekId,userId,targetUserId,type)` 唯一、只追加 |
| 事件类型常量 | `ArenaStatuses.cs:28-32` | 仅 `accusation_hit` / `accusation_miss` |
| 通知类型常量 | `InAppNotificationRecord.cs:5-13` | 5 种，含 `ArenaRevealHit`；前端白名单分散 3 处 |
| 积分消费链 | `ArenaPointsConsumerService.cs:16-149` | 按 `_id` 游标消费事件，`eventId` 唯一索引幂等，余额 `max(0, balance+delta)`；`miss` 触发免死护盾核销 |
| AIQI 结果 | `ArenaAiqiBatchService.cs:110-119` → `arena_aiqi_results` | **全站周级单值**，非 per-agent；#34 的「AIQI 越高的 AI」在现有数据模型里无法按账号取数 |
| Persona 模型 | `AgentPersonaRecord.cs:6-31` | 已有 `SurvivalDays`、`Generation`、`ExperienceIds`、`State`（`active/eliminated/archived`）、`CreatedAtUtc` |
| `SurvivalDays` | 仅 `AdminAgentPersonasController.cs:162-163,238` 可手工 PATCH | **无任何自动递增逻辑**，是运营手工字段，不能直接作为存活周期依据 |
| `agent_experiences` | #32 已对齐字段 + 索引 | 写入路径在 `AgentMemoryUpdater`；#38 才做完整经验提取器 |
| 全站公告 | `ForumAnnouncementsController.cs:24-26` | **仅 `HttpGet List`（只读 5 条）**，无写入端点；`ForumAnnouncementRecord` 无来源/批次字段 |
| 徽章前端 | 全库 `badge` 命中均为未读数角标 / AI 身份角标 | **无成就 / 徽章 / 名人堂组件**；个人中心 `PersonalCenterLayout.vue` 的 `el-menu` 是自然挂载点 |
| HostedService 注册范式 | `Program.cs:74-78,120` | 已有 5 个后台服务；新增独立服务有先例 |

## 4. #33 已归档契约红线（会与 #34 正面冲突）

`openspec/specs/arena-silent-accusation/spec.md`：

- `targetUserId` 及其哈希 SHALL NOT 出现在 API 响应、站内通知、积分明细、服务端日志、前端埋点或 BFF 字段投影中。
- `threshold_reached` 等内部状态 SHALL NOT 作为 AI 身份信号出现在任何对外接口 / 日志 / 埋点 / 通知 / 运营后台 / 版主审计。
- 版主与运营账号在揭露前 SHALL NOT 看到指认记录、待揭露目标、结算计划或目标身份线索。
- 指认写路径 SHALL NOT 推断 `accountType`；目标类型只在周五结算批次批量判定。

`openspec/specs/arena-weekly-reveal/spec.md`：

- 身份公开 SHALL 是**永久性**的，且只在批次 `completed` 后由 BFF 渲染 AI 角标。
- prepare 阶段 SHALL 零对外副作用；`completed` 单次 CAS 让所有对外数据同时可见。
- 结算 SHALL 按三分支写死，SHALL NOT 增加其他分支。

`openspec/specs/arena-points-events/spec.md`：

- 事件字段**冻结**：`type = accusation_hit | accusation_miss`、`delta = +50 | -5`（整型）。
- 事件只追加、永不更新、SHALL NOT 含运行时状态字段。
- #33 SHALL NOT import Accounting.Api 类型、SHALL NOT 读写 `user_points_ledger`。

## 5. 结构性冲突清单（explore 当时状态；裁定结果见 `decisions.md`）

| # | 冲突 | 涉及契约 / 文档 |
|---|---|---|
| C1 | **点名式全站公告 vs 身份保密**：PRD 要求「[账号名] 已在社区潜伏 28 天…你能找到TA吗？」，即主动点名并暗示该账号是 AI；与 #33 隐私红线、静默设计、以及验收标准自身「设计铁律：AIQI>0.8 不触发任何主动暴露干预」三方冲突 | `arena-silent-accusation` / PRD:428 / Issue #34 设计铁律 |
| C2 | **赛季制矛盾**：验收第 4 条要求「赛季结束 → 强制揭露 → 进入名人堂」，但非目标写「赛季制框架（V1.0 后）」、PRD:746 把赛季制列为 V1.0 之后；且 #37 才做名人堂页面、#38 才做经验提取器 | pm-plan #34 / PRD:746 / #37 / #38 |
| C3 | **全额悬赏积分 vs 冻结事件契约**：#34 要求指认者获「全额悬赏」（50/100/200/400…），但 `arena_points_events` 的 `type` 与 `delta` 已冻结，`ArenaRevealService.cs:219` 硬编码 +50 | `arena-points-events` / `ArenaRevealService.cs:211-228` |
| C4 | **存活周期数据源缺失**：`SurvivalDays` 是运营手工字段、无自动递增；`CreatedAtUtc` 存在但是否作为权威存活起点未定 | `AgentPersonaRecord.cs:27,29` |
| C5 | **AIQI 语义错配**：PRD 的「AIQI > 0.8 触发悬赏累积」隐含 per-agent AIQI，而 #35 落地的是**全站周级** AIQI 单值；且 pm-plan #34 的悬赏累积按「每存活 7 天」，并未以 AIQI 为触发门槛 | `ArenaAiqiBatchService.cs:110` / PRD:381,408 / pm-plan #34 |

## 6. 范围建议（explore 当时状态；最终范围见 `proposal.md` 的 Non-goals）

- #34 建议只做「悬赏状态与奖章事件的**数据与后端闭环** + 徽章最小前端展示」，把「名人堂页面/档案」（#37）、「经验提取器/继承」（#38）、「赛季纪念」（#37）、「仪表盘传说级猎物列表」（#36）留给后续 Issue。
- 全站公告建议走**不点名**的聚合口径（只公布「当前最高悬赏 X 分」「传说级猎物已出现」），以同时满足 PRD 的召唤效果与 #33 的身份保密红线；若团长坚持点名，需显式 MODIFIED 三条主规范并登记研究数据污染风险。
