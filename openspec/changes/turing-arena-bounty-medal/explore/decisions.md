# 团长裁定记录 · Issue #34

> 日期：2026-11-14 ｜ 阶段：/opsx:explore 收口 → propose 基线
> **两轮共 7 项裁定**（首轮 4 项结构性冲突 + 调研后半程浮现的 3 项），全部按推荐方案裁定，作为 propose / design / delta spec 的约束基线。
> 2026-11-14 补记：第二轮 3 项已由团长确认，原第 9 条「待团长确认」状态解除；**全表无待定项**，可进入 propose。

## 首轮：4 项结构性冲突（C1–C5）

| # | 冲突 | 裁定 | 对设计的硬约束 |
|---|---|---|---|
| C1 | 点名式全站公告 vs #33 身份保密红线 + 设计铁律 | **不点名聚合** | 公告 SHALL NOT 含账号名 / `targetUserId` / 任何可反推具体账号的字段；只发聚合信息（如「本周出现传说级猎物，当前最高悬赏 400 分」）。零 MODIFIED `arena-silent-accusation`；`arena-weekly-reveal` 的可见性门禁不得绕过。 |
| C2 | 赛季结束强制揭露 vs 非目标「赛季制 V1.0 后」+ #37/#38 边界 | **只做数据标记** | #34 只落「传说级猎物 / 图灵幸存者 / 图灵猎手徽章」等状态与标记数据；**不做**强制揭露、不做名人堂页面、不做经验提取器（分别归 #37 / #38）。验收第 4 条在本期以「打标记 + 产出可供后续 Issue 消费的记录」为准，需在 proposal 显式登记范围裁剪。 |
| C3 | 全额悬赏 vs 冻结的 `arena_points_events` 契约 | **扩展事件契约** | MODIFIED `arena-points-events`：把 `delta` 从冻结 `+50` 放开为按目标悬赏值取的可变正整型；沿用 #35 消费者 `_id` 游标 + `eventId` 唯一索引幂等自动入账；依赖方向保持 `#34 → #33 → #35` 单向，#34 不写 `user_points_ledger`。**具体实现形态按下方第 9 条 + 第二轮 N2 收紧**：不新增 `type`、不新增字段，只放开 `delta` 语义。 |
| C4/C5 | 存活周期数据源缺失 + AIQI 语义错配 | **`CreatedAtUtc` 计时** | 以 `persona.CreatedAtUtc` 为权威起点、按完整 7 天周期计数，周批次 `weekKey` 幂等；`SurvivalDays` 手工字段不得作为计时源。悬赏累积**不设 AIQI 门槛**；`AIQI > 0.8` 仅作为「禁止降低 AI 质量 / 禁止主动暴露干预」的保护性断言（需在规范与测试中显式覆盖）。不新增 per-agent AIQI。 |

## 第二轮：3 项新增裁定（调研后半程浮现，2026-11-14 已确认）

| # | 冲突 | 裁定 | 对设计的硬约束 |
|---|---|---|---|
| N1 | **多指认者积分经济**：#33 对每位成功指认者各写一条 `accusation_hit`（阈值默认 3 人）。悬赏 400 分时若每人全额，单次结算投放 = N × 400（最多约 1200 分），会常态触发 #35 通胀告警 | **每人全额 + 封顶** | ① 每位成功指认者 SHALL 各获一条 `accusation_hit`，`delta` **恰为该目标当前 `weekKey` 的悬赏值**，SHALL NOT 按人数分摊、SHALL NOT 除以 N、SHALL NOT 只奖励前 K 人。② **双层封顶**：悬赏值硬上限 `CapPoints`（默认 400，可配）+ 单周结算总投放上限 `MaxPayoutPerWeek`（默认 `CapPoints × RevealThreshold`，可配）。③ 总投放超额时 SHALL 记录运营告警、SHALL NOT 削减已判定指认者的金额——削减会破坏 #33 verdict 表「其全部 counted 指认者 SHALL 获得 `accusation_hit`」与 `eventId` 幂等语义；由运营通过调低 `CapPoints` 或 `Enabled=false` 干预。④ 悬赏导致净流量上升属预期，`InflationAlert` 语义在 G4 确认可接受，SHALL NOT 视为回归。 |
| N2 | **确认 C3 实现形态收紧**：首轮 C3 文字写「新增 bounty 类事件 `type`」，但四份调研实证新增 type 会污染 AIQI 分母、新增字段会让 #35 消费方反序列化异常、新增 type 会在前端回显英文 key | **确认收紧** | 最终形态锁定为「**不新增 `type`、不新增字段、只把 `delta` 从冻结 +50 放开为悬赏值**」。`type` 枚举保持 `accusation_hit \| accusation_miss`；`eventId` 派生不变；事件只追加、无状态字段不变；Accounting.Api **零代码改动**；前端 `PointsPanel.vue` 无需新增 `typeLabel`。复用全仓唯一写入点 `ArenaRevealService.cs` 的 `Delta = 50`，缺省仍恰好 +50。此为 C3 的实现形态收紧，方向仍属 C3 范围。 |
| N3 | **#33 in-progress 缺口 + 范围裁剪**：`reveal_jobs` 卡在 `settling/settled/revealing` 时该周返回 `Skipped` 且全仓无接管路径，永不重算。悬赏数值靠纯函数下一周可自愈，但「满 4 周奖章公告」这类一次性事件会永久漏发 | **登记缺口 + 本期自愈** | 采用 architect D4 方案 ①：**零改动 #33 状态机**，仅登记该既有缺口为已知技术债；本期一次性事件触发条件 SHALL 使用「`survivedWeeks >= MedalWeeks` **且** `MedalAwardedAtUtc == null`」下界 + 未发放标记，配合 `arena_bounty_runs` 幂等，使被 Skipped 的周在后续补跑时恰好发放一次。SHALL NOT 把「补 in-progress 续跑」列为 #34 前置 P0（那会改 `ArenaRevealService` 状态机，超出范围）。缺口本身作为独立 Issue 候选登记，不在 #34 修复。 |

## 裁定的连带结论

1. **隐私红线不松动**：`arena-silent-accusation` 的 `targetUserId` 禁令、`threshold_reached` 不得外泄、版主/运营揭露前零可见，全部继续有效。C1 的聚合公告必须在需求里写明「不含任何账号标识」，并纳入侧信道测试。
2. **不引入赛季概念**：本期 SHALL NOT 新建 `season` / 赛季结束判定 / 强制揭露分支。原验收「赛季结束仍未被发现」降级为「存活满可配置 N 周且仍未被揭露 → 打幸存者标记」，供 #37 消费。
3. **#33 结算分支保持三分支写死**：#34 的悬赏倍数不得作为 `ArenaRevealService` 里的第四条 verdict 分支；应通过「结算前读取悬赏状态 → 计算 delta」的方式注入，结算状态机不变。
4. **通知与公告分离 + 零门禁字段**（四份调研交叉复核后收紧）：全站公告由后台服务直接 `InsertOne` 写 `forum_announcements`，**不新增 HTTP 写端点**（`ForumAnnouncementsController` 保持只有 `HttpGet List`）；`ForumAnnouncementRecord` **零字段扩展**，不引入 `BatchId/ReleasedAtUtc/AnnouncedAtUtc/IsPublic` 中的任何一个。理由：C1 不点名后公告零身份信息、无需隐藏中间态；引入独立门禁会与 `arena-weekly-reveal` 的「**唯一**对外可见性门禁」表述冲突。幂等改用固定 `Id = "arena-bounty-medal-" + weekKey` + 11000 静默跳过；公告 SHALL NOT 设 `linkUrl`（`/arena/hall-of-turing` 路由尚不存在，属 #37 规划页）。个人 +积分通知沿用 #33 的 `ArenaRevealHit` 通道，金额改为悬赏值。
5. **`forum-content-api` 无需 MODIFIED**：`openspec/specs/forum-content-api/spec.md:286` 的「read-only」修饰的是**端点**（只有 GET），不是集合；该 Requirement 只约束 `GET /api/forum/announcements` 的字段契约。后台服务写集合不违反它。
6. **MODIFIED delta 必须逐条改写 `+50` 文字**（否则 change-review 会拦，四份调研交叉核对后的完整清单）：
   - `arena-points-events/spec.md`：**`:13`**（`delta = +50 | -5` 冻结条款）、**`:35`**（「常量标记、非发送状态」双保险说明中的「+50 通知缺失」）、`:51`（「成功指认者的 +50 积分站内通知」）、`:53`（时序侧信道理由「是否收到 +50」）、`:58`（F-07 断言第 2 条「恰好 1 条 +50 通知」）、`:71`（场景 THEN「恰好收到 1 条 +50 站内通知」）。
   - `arena-weekly-reveal/spec.md`：`:35`（`InsertMany` 批量插入 +50 通知）、`:49`（场景「+50 通知…不可见」）、`:64`（场景标题「Unread count does not leak a prepared batch」下的 +50 通知已插入）、`:80`（verdict 表的「+50 事件」）。
   - 实现侧：`ArenaRevealService.cs:17`（类注释「批量 +50 通知」）、`:219`（`Delta = 50`）、`:226`（行内注释「+50 通知」）；`ArenaRevealPipelineTests.cs:14/:97/:104/:109/:251` 的硬编码断言与注释。
   - 产品端：`docs/PRD-turing-arena.md:292/:337`、`scripts/github-sync/pm-plan.yaml:303` 的「+50」为 #33 基线描述，属历史事实，**不改**；仅 #34 新增段落用悬赏值表述。
   - 通知**机制**（`ArenaRevealHit`、`uniq_eventId` 幂等、门禁时机）不变，仅金额与文案改为「悬赏值」。
7. **范围裁剪登记**：proposal SHALL 显式列出「本期不做」：名人堂页面与档案（#37）、经验提取器/继承（#38）、仪表盘传说级猎物列表（#36）、赛季纪念（#37）、赛季制框架（V1.0 后）、强制揭露分支。
8. **需求变更登记**：`pm-plan.yaml:328` 与 GitHub Issue #34 正文的明文点名文案「[账号名] 已在社区潜伏 N 天…」按 C1 裁定改为聚合口径，归档时同步改这两处，避免 spec 与产品端文案打架。
9. **C3 实现形态收紧已确认**（原「待团长确认」，第二轮 N2 已点头）：最终形态为「**不新增 type、不新增字段、只把 delta 从冻结 +50 放开为悬赏值**」（复用全仓唯一写入点 `ArenaRevealService.cs:214`，由 #35 现有消费者自动入账）。三条实证理由：① `ArenaAiqiBatchService.cs:87` 把 `weekEvents.Count` 计入 AIQI 分母，新 type 会稀释检测成功率；② 生产/消费事件模型都无 `[BsonIgnoreExtraElements]`，新增字段会让 #35 反序列化抛异常；③ `PointsPanel.vue:9-14` 未映射 type 会回显英文 key。据此写入 delta spec，无需再请示。
10. **编译级改动面（propose 收口时补记）**：若给 `ArenaRevealService` 构造函数新增 `IOptions<ArenaBountyOptions>` / `IArenaBountyService`，`ArenaRevealPipelineTests.cs:32-38` 与 `ArenaRevealServiceTests.cs:33-39` 两个 `CreateService()` 均为**位置传参**，会直接编译失败。tasks SHALL 明确同步这两个 helper；SHALL NOT 采用服务定位器绕开签名（反模式）。
11. **徽章只读端点（propose 收口补记，方案变更已登记）**：D7 要求个人中心徽章只读视图，但四张新集合无对外读取路径。新增 `GET /api/arena/hunter-badge/me`（Model.Api、`[Authorize]`、按 JWT `sub` 隔离），只返回当前认证用户徽章，SHALL NOT 暴露其他用户或目标账号标识。该端点**不是公告写入端点**，故不违反第 4 条「不新增 HTTP 写端点」；`proposal.md` 原「不新增 HTTP 端点」措辞已收紧为「不新增公告写入端点」，ADDED spec 的徽章 Requirement 已补只读端点契约与场景。Vite `/api` 代理经 Gateway `arena-route → model-service` 可达。
