# 测试清单 · Issue #34 悬赏累积与图灵奖章事件

> 全部为 CI 可自动执行的 Mongo2Go 自包含用例，测试类前缀 `ArenaBounty*`，自动落入现有门禁 `--filter "FullyQualifiedName~Arena|FullyQualifiedName~Agent"`（ci.yml 无需改动）。
> 末尾 3 项为人工/盲测闸门，不进 CI。

## A. 悬赏累积（D1/D2/D3）

| # | 用例 | 断言 |
|---|---|---|
| A1 | 第 1/2/3/4 周悬赏推导 | `CreatedAtUtc` 固定下，四个 `weekKey` 分别得到 50 / 100 / 200 / 400 |
| A2 | 上限收敛 | 第 5、8、12 周均为 `CapPoints`（默认 400），不溢出 int、不出现 800 |
| A3 | **补跑 12 周不重复翻倍**（P0） | 模拟 `CatchUpAsync` 连续 12 个 `weekKey`，`CurrentBounty` 与按最终 `weekKey` 纯函数计算值一致；重复跑同一 `weekKey` 结果逐字节一致 |
| A4 | `Set` 而非 `Inc` 幂等 | 同一 `weekKey` 连续计算 3 次，集合仅 1 条文档、`LastComputedWeekKey` 不变 |
| A5 | 手工 `SurvivalDays` 不影响结果 | 把 persona 的 `SurvivalDays` 设为 999，悬赏仍由 `CreatedAtUtc` 推导 |
| A6 | 周键口径 | 使用 #33 `ArenaWeekKeys`（Asia/Shanghai 周一），与 `ArenaRevealSchedule.ComputeRevealWeekKey` 一致；不用 UTC 周键 |
| A7 | 已揭露账号停止累积 | `state=eliminated` 的账号在后续周批次中 `CurrentBounty` 与 `SurvivedWeeks` 均不变 |
| A8 | 灰度关闭 | `Enabled=false` 时零写入 `arena_bounty_states`、零公告，结算积分回落 +50 |
| A9 | 计算失败不阻断揭露 | stub 抛异常时，`reveal_jobs` 仍推进到 `completed`，且本周不写悬赏状态 |
| A10 | 边界：存活不足 7 天 | `survivedWeeks=0` 时不写悬赏状态、不发公告 |
| A11 | 补跑 12 个不同 weekKey | 模拟 `CatchUpAsync:87-97`，12 个不同 `weekKey` 各自独立计算；`LastComputedWeekKey` 单调推进，最终值等于最新周纯函数结果，不与第 1 周结果混淆 |
| A12 | 消费游标不跳不重（复用写入点） | 事件 `Id` 为 `ObjectId.GenerateNewId().ToString()`（`ArenaPointsEventRecord.cs:12`），`ArenaPointsConsumerService.cs:67-71` 的 `Gt(Id, lastId)` 游标能顺序消费、不漏不重 |
| A13 | 三套周键的唯一权威 | 悬赏计算与 `WeekId` 均取 #33 `ArenaWeekKeys`（Asia/Shanghai 周一）；**不**使用 #35 `ArenaAiqiBatchService.StartOfWeek`（UTC 周日起算，全库**无** `ArenaMetricWeek` 类）或前端 `arenaAnalytics.ts:64 getArenaWeekKey`（浏览器本地时区）；新增埋点 `weekKey` 由后端下发或显式按 +8 计算 |
| A14 | **in-progress 周不漏发一次性事件**（P0） | 模拟某周 `reveal_jobs` 卡在 `settling` 被 `Skipped`：数值类下一周按 `CreatedAtUtc` 重算自愈；奖章公告/幸存者标记 SHALL 以「`survivedWeeks >= MedalWeeks` 且 `MedalAwardedAtUtc == null`」触发，补跑后恰好发一次，不因「恰好第 4 周」被跳过而永久漏发 |
| A15 | 按账号 × weekKey 幂等 | `arena_bounty_runs` 唯一索引 `{AgentUserId, WeekKey}`，同一账号同一周重复 `ComputeWeekAsync` 遇 11000 静默跳过；12 个不同 weekKey 各自独立领取 |
| A16 | 历史 persona 无 CreatedAtUtc | 存量账号 `CreatedAtUtc` 缺失/为 `DateTime.MinValue` 时 SHALL 跳过悬赏计算并记日志，SHALL NOT 抛异常、SHALL NOT 阻断结算 |

## B. 积分事件（D5）

| # | 用例 | 断言 |
|---|---|---|
| B1 | 传说级猎物全额悬赏 | 悬赏 400 时，每个成功指认者事件 `Delta=400` 且 `type=accusation_hit` |
| B2 | 普通目标保持 +50 | 非传说级目标仍 `Delta=50` |
| B3 | **零新增字段 + 事件只追加** | 事件文档字段集合 SHALL 与 `ArenaPointsEventRecord.cs:10-22` 完全一致（无 `bountyPoints` 等新字段）；`type` 仅 `accusation_hit|accusation_miss`；通知投递前后该事件文档**零 update**；#35 消费方 `ArenaSourceEventRecord` 反序列化不抛异常（两模型均无 `[BsonIgnoreExtraElements]`） |
| B4 | `eventId` 幂等 | 同一 `weekKey` 重放结算，唯一索引阻止第二条事件 |
| B5 | **AIQI 口径不变**（P0） | 用含悬赏事件的样本跑 `ArenaAiqiBatchService.RunWeekAsync`，hits / 指认总数 / AIQI 与「+50 基线」样本完全一致（证明未因 type 变化漏计） |
| B6 | #35 消费者自动入账 | `ArenaPointsConsumerService` 消费悬赏事件后余额增量 = 悬赏值；`eventId` 唯一索引幂等 |
| B7 | miss 分支不变 | human 目标仍 `Delta=-5`、`notifyEligible=false`、零通知 |
| B8 | 依赖方向 | 静态检查 Model.Api 未 import Accounting.Api、未读写 `user_points_ledger`；Accounting.Api **零代码改动** |
| B9 | 幂等键按 key 不按值 | 四字段唯一索引 `{WeekId,UserId,TargetUserId,Type}` 不含 `Delta`；同一 `weekKey` 重放时原金额保持不变（11000 静默跳过），不同周的悬赏值各自独立，不出现「同 key 不同 Delta」 |
| B10 | 缺省回退恰好 +50 | 灰度关闭 / 悬赏状态缺失 / 计算失败时，`Delta` SHALL 恰好为 50；`ArenaRevealPipelineTests.cs:97-109` 的既有断言零改动仍全绿 |
| B11 | `+50` 规格文字已同步 | `arena-points-events/spec.md:13/:35/:51/:53/:58/:71` 与 `arena-weekly-reveal/spec.md:35/:49/:64/:80` 的 `+50` 措辞均已 MODIFIED；无残留「+50 冻结」表述 |
| B12 | **多指认者口径**（N1 已裁定「每人全额 + 封顶」） | 每位成功指认者 SHALL 各获一条 `accusation_hit` 且 `Delta` 恰为当前悬赏值，SHALL NOT 按人数分摊 / 除以 N / 只奖励前 K 人；悬赏值 SHALL NOT 超过 `CapPoints`；单周总投放超过 `MaxPayoutPerWeek` 时 SHALL 记录运营告警、**SHALL NOT 削减任何已判定指认者的金额**（削减违反 #33 verdict 表与 `eventId` 幂等语义）；`InflationAlert` 语义可接受即不视为回归 |

## C. 聚合公告（D6 / C1 隐私红线）

| # | 用例 | 断言 |
|---|---|---|
| C1 | 公告零账号标识 | 公告 `Title`/`Summary`/`LinkUrl` 中 SHALL NOT 出现账号名、`targetUserId`、其哈希、存活天数与账号的对应关系；用真实 persona 昵称做反向包含断言 |
| C2 | 仅聚合口径 | 文案只含「最高悬赏」「传说级猎物出现」等聚合计数；不含可枚举单账号的线索 |
| C3 | **零门禁字段 / 零字段扩展** | `ForumAnnouncementRecord` 序列化后 SHALL 只含 `Id/Title/Summary/LinkUrl/PublishedAtUtc/Pinned`；断言不存在 `BatchId/ReleasedAtUtc/AnnouncedAtUtc/IsPublic` 任一字段；`ForumAnnouncementsController` 仍只有 `HttpGet List`、无写端点 |
| C4 | 写入即可见且幂等 | `Id = "arena-bounty-medal-" + weekKey` 固定派生，`InsertOne` 遇 11000 静默跳过；重复跑同一 `weekKey` 公告恰好 1 条 |
| C5 | 不设 linkUrl | `LinkUrl` 为 null（`/arena/hall-of-turing` 路由不存在，属 #37 规划页）；`HomeView.vue:337` 走静态标题分支，无死链 |
| C6 | 版主/运营零可见 | 版主与运营账号在揭露前查询治理、审计、通知、公告视图均无身份线索 |
| C7 | 不复用通知集合 | 全站公告不向 `forum_in_app_notifications` 写任何 per-recipient 文档 |
| C8 | 埋点白名单 | 前端埋点经 `assertAllowedAnalyticsKeys`，仅 `ts / accuserIdHash / weekKey / 聚合计数`；新增悬赏/徽章埋点需先扩白名单，否则 `throw` |
| C9 | 门禁唯一性 | 断言 `arena-weekly-reveal` 的「唯一对外可见性门禁」未被 #34 引入第二个门禁；公告不属于结算批次新增数据 |
| C10 | 前端契约不变 | `GET /api/forum/announcements` 响应字段契约与 `forum-content-api/spec.md:286` 一致（id/title/summary/linkUrl/publishedAtUtc/pinned），首页无需改动 |

## D. 徽章与幸存者标记（D7/D8）

| # | 用例 | 断言 |
|---|---|---|
| D1 | 徽章授予 | 成功指认传说级猎物后 `arena_hunter_badges` 恰好 1 条，字段含 `TargetWeekKey` / `BountyPoints` |
| D2 | 徽章幂等 | 重放结算遇 11000 静默跳过，仍 1 条 |
| D3 | 未获奖者无徽章 | 普通目标 / 错误指认者 / 被指认方均无徽章 |
| D4 | 幸存者只打标记 | 存活满 `SurvivorWeeks` 且 active 时写 `SurvivorMarkedAtUtc`；`agent_experiences` 零新增、`persona.State` 仍 `active`、**无强制揭露** |
| D5 | 不产生名人堂数据 | 无 `/arena/hall-of-turing` 路由、无赛季纪念生成 |

## E. 设计铁律与结算状态机（D9 / #33 回归）

| # | 用例 | 断言 |
|---|---|---|
| E1 | 铁律断言 | 悬赏累积路径不读 `arena_aiqi_results`、不 import Accounting.Api；高 AIQI 不降低提示词质量、不暂停账号、不提前揭露 |
| E2 | 三分支不变 | `ArenaRevealService` 结算仍为 agent-hit / human / agent-below-threshold 三分支，无第四分支 |
| E3 | #33 全量回归 | 现有 `ArenaReveal*` / `ArenaAccusation*` 测试集全绿（阈值、自白帖、屏蔽词、通知门禁、归档清理） |
| E4 | 身份永久公开不回退 | 历史已揭露账号的 AI 角标、自白帖、身份在后续 prepare 期仍可见 |
| E5 | CI 计数 | `Arena|Agent` 过滤器实跑数 = 既有 91 + 本期新增用例数，0 失败 |

## F. 人工 / 盲测闸门（不进 CI）

| # | 闸门 | 方法 |
|---|---|---|
| H1 | 公告隐私盲测 | 让不知情测试者读聚合公告，尝试反推具体账号；应无法定位，且不改变其指认行为基线 |
| H2 | 徽章前端观感 | 个人中心徽章展示在亮/暗主题、长昵称、窄屏下无溢出、无硬编码色、标题 2 行截断 |
| H3 | 补跑演练 | 在测试环境重启服务触发 12 周补跑，人工核对悬赏值与公告条数无异常放大 |

## 回归基线

- 合并前 SHALL 跑：`dotnet build JIssWeb.sln`（0 错 0 警）、`--filter "FullyQualifiedName~Arena|FullyQualifiedName~Agent"`（91 + 新增全绿）、前端 `npm test -- --run`（12 文件 / 55 项基线全绿）、`openspec validate --all`（60 + 新增 delta 全绿）。
- 既有 42 条 Model.Api 环境性失败（缺本地 Mongo/JWT/版主配置）保持零新增回归。
- **B 类硬断言精确化**：`ArenaRevealPipelineTests.cs` 中真正会被 `Delta` 语义改到的只有 `:104` `Assert.Equal(50, e.Delta)`；`:165` 的 `-5` 断言不受影响；`:14/:97/:109/:251` 是注释/计数断言。因 `ArenaBountyOptions.Enabled` 默认 `false`，既有用例在**不改一行断言**的前提下仍全绿；悬赏值断言由新增 `ArenaBounty*` 用例承担（B1/B2/B10）。
- **公告门禁作用域断言**（E6）：`arena-weekly-reveal` 的「唯一对外可见性门禁」Requirement 补作用域澄清后，断言该门禁仍只覆盖结算批次产出数据；聚合公告不受其约束、也无独立门禁字段。
