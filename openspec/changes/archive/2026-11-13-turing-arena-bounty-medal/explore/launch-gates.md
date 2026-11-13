# 上线闸门 · Issue #34 悬赏累积与图灵奖章事件

> 三道自动闸门 + 两道人工闸门，合并前 SHALL 全部通过。沿用 #33 的 G1/G2/G3 编号体系，本期扩为 G1–G5。

## G1 · 测试门禁（CI 自动）

- `dotnet build JIssWeb.sln` → 0 错 0 警。
- `dotnet test ... --filter "FullyQualifiedName~Arena|FullyQualifiedName~Agent"` → 既有 91 条全绿 + 本期 `ArenaBounty*` 新增全绿，**0 失败**。
- 前端 `npm test -- --run` → 12 文件 / 55 项基线全绿（若新增徽章组件则含新增用例）。
- `openspec validate --all` → 60 份主规范 + 本期 delta 全绿。
- 既有 42 条 Model.Api 环境性失败（缺本地 Mongo/JWT/版主配置）**零新增回归**。
- 断言覆盖 `test-checklist.md` 的 A–E 全表，尤其 P0 项 A3（补跑 12 周不重复翻倍）、A11（12 个不同 weekKey）、B5（AIQI 口径不变）、C1（公告零账号标识）、E3（#33 全量回归）。

## G2 · 灰度默认关（配置审查）

- `ArenaBountyOptions.Enabled` 默认 `false`；关闭时 SHALL 零写入 `arena_bounty_states`、零公告，结算积分**回落到 #33 的 +50 基线**。
- 开关打开前 SHALL 通过一次测试环境全量演练；生产按周逐步放量，观察 `arena_netflow_results` 的 `InflationAlert` 无异常放大。
- 配置项：`BasePoints=50` / `CapPoints=400` / `MedalWeeks=4` / `SurvivorWeeks=8` / `AnnouncementEnabled=true`，全部可配置且 SHALL NOT 硬编码。

## G3 · 隐私与身份红线（自动 + 人工）

- 公告 `Title/Summary/LinkUrl` SHALL NOT 含账号名、`targetUserId`、其哈希、存活天数与账号的对应关系、任何可枚举单账号的线索（C1/C2）。
- 悬赏数据、结算计划、徽章授予记录 SHALL NOT 作为 AI 身份信号出现在对外接口 / 日志 / 埋点 / 通知 / 运营后台 / 版主审计。
- 版主与运营账号在揭露前查询治理、审计、通知、公告视图，SHALL 零身份线索（C6）。
- 前端埋点经 `assertAllowedAnalyticsKeys` 白名单，仅 `ts / accuserIdHash / weekKey / 聚合计数`（C8）。
- **#33 三道原有闸门继续全绿**：human 目标零感知；版主/运营看不到揭露前的指认与待揭露记录；指认后至揭露前零通知。

## G4 · 契约与经济口径（自动）

- **AIQI 口径不变**：`ArenaAiqiBatchService` 的 `hits/misses/totalAccusations/ActiveUsers/DetectionSuccessRate/AIQI/净流量` 在「+50 基线样本」与「悬赏样本」下除净流量金额外完全一致（B5）。悬赏导致净流量上升属预期，需确认 `InflationAlert` 语义可接受。
- **单向依赖不变**：Model.Api SHALL NOT import Accounting.Api、SHALL NOT 读写 `user_points_ledger`；事件 `type` 枚举与字段集合 SHALL 与 #33 完全一致。
- **唯一门禁不变**：`reveal_jobs.state=completed` 仍是结算批次的唯一对外可见性门禁；公告独立事件不引入第二个门禁（C9）。
- **#33 结算状态机不变**：三分支写死、prepare 零对外副作用、`completed` 单次 CAS 全量可见（E2/E3/E4）。
- **积分投放上限（第二轮 N1 已裁定「每人全额 + 封顶」）**：每位成功指认者 SHALL 各获一条 `accusation_hit`，`delta` 恰为当前悬赏值，SHALL NOT 按人数分摊 / 除以 N / 只奖励前 K 人（削减或截断名单会违反 #33 verdict 表「其全部 counted 指认者 SHALL 获得 accusation_hit」）。双层封顶：悬赏值硬上限 `CapPoints`（默认 400，可配）+ 单周结算总投放上限 `MaxPayoutPerWeek`（默认 `CapPoints × RevealThreshold`，可配）；超额时 SHALL 记录运营告警、SHALL NOT 削减已判定金额，由运营调低 `CapPoints` 或 `Enabled=false` 干预。悬赏导致净流量上升属预期，`InflationAlert` 语义可接受即不视为回归。

## G5 · 人工 / 盲测（合并前团长签字，不进 CI）

- **H1 公告隐私盲测**：让不知情测试者读聚合公告，尝试反推具体账号；应无法定位、且不改变其指认行为基线。
- **H2 徽章前端观感**：个人中心徽章在亮/暗主题、长昵称、窄屏下无溢出、无硬编码色、标题 2 行截断、不用 `:deep` 覆盖 EP（遵守 `.cursor/rules/forum-ui.mdc`）。
- **H3 补跑演练**：测试环境重启触发 12 周补跑，人工核对悬赏值与公告条数无异常放大、无重复公告。

## 上线顺序

1. 合入代码但 `Enabled=false`（默认）→ 观察一个结算周期，确认 #33 行为零变化。
2. 测试环境打开开关，跑满 4 周或用 fixture 快进 4 个 `weekKey`，核对悬赏序列 50/100/200/400 + 上限收敛。
3. 生产灰度：先开 `AnnouncementEnabled=false` 只验证数据正确性，再开公告。
4. 全量后连续观察 2 个结算周期的 AIQI、`InflationAlert` 与 WAD（北极星指标），无异常即闭环。

## 回滚

- 一级回滚：`Enabled=false`，悬赏/公告/徽章全部停写，积分回落到 +50 基线；**无需回滚代码**。
- 二级回滚：`git revert` 本期提交；`arena_bounty_states` / `arena_bounty_medals` / `arena_hunter_badges` 为独立新集合，删除不影响既有数据。
- SHALL NOT 回滚 `arena_points_events` 已写入事件（只追加契约）；若已发出悬赏金额，按事件事实保留、不再补发。
