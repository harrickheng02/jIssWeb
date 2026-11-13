## Purpose

本规范为 `arena-points-events` 能力的归档基线，汇总历次 OpenSpec 变更产出的 4 条需求及其验收场景（Scenario），作为后续变更的 MODIFIED / ADDED delta 的合并目标。
## Requirements
### Requirement: Immutable points event contract

Model 服务 SHALL 在周五结算批次内产出 `arena_points_events` 事件，作为 #33 的唯一积分事实源。字段 SHALL 冻结为：

```
eventId / userId / type = accusation_hit | accusation_miss
/ delta = 悬赏值（正整型，缺省 +50）| -5（整型） / targetUserId / accusationId / weekId
/ occurredAt / notifyEligible
```

`type` 枚举 SHALL 冻结为 `accusation_hit` 与 `accusation_miss` 两类，字段集合 SHALL 冻结；系统 SHALL NOT 新增悬赏类事件类型，SHALL NOT 新增任何字段（含悬赏分数字段）。`delta` SHALL 为整型：成功指认（`accusation_hit`）的绝对值 SHALL 恰等于该目标在当前 `weekKey` 的悬赏值（基础分默认 50、上限可配置），错误指认（`accusation_miss`）SHALL 固定为 -5。悬赏值缺省（能力开关关闭、悬赏状态缺失或计算失败）时，`delta` SHALL 恰好为 50。悬赏值 SHALL 仅通过 `delta` 表达，审计 SHALL 可直接读取事件文档。

`eventId` SHALL 以 `(weekId, userId, targetUserId, type)` 建唯一索引，该唯一键 SHALL NOT 含 `delta`。事件 SHALL 只追加、永不更新、SHALL NOT 含任何运行时状态字段（如投递状态、消费状态、重试计数）。

系统 SHALL NOT 因悬赏机制改变事件 `type` 集合或漏计指认；#35 周级 AIQI 跑批的 `hits` / `misses` / `totalAccusations` / `DetectionSuccessRate` / `AIQI` 口径 SHALL 与基础分基线样本完全一致，仅积分净流量金额随悬赏值变化。

`targetUserId` SHALL 仅供 #35 核对与幂等使用，SHALL NOT 出现在 API 响应、站内通知、积分明细、服务端日志或前端埋点（含哈希）中。

#33 SHALL NOT import `JIssWeb.Accounting.Api` 的类型或调用其服务；SHALL NOT 读取或写入 `user_points_ledger`；SHALL NOT 回填历史事件。依赖方向 SHALL 单向为 `#35 → #33`；账务侧 SHALL 无需代码改动即可按既有消费链自动入账。

#### Scenario: Replaying a settlement produces no duplicate event

- **WHEN** 同一 `weekKey` 的结算批次因故障续跑
- **THEN** `eventId` 唯一索引 SHALL 阻止重复事件
- **AND** 每个成功指认者 SHALL 恰好保留一条 `accusation_hit` 事件
- **AND** 重放 SHALL NOT 改变原事件的 `delta` 金额

### Requirement: notifyEligible is a creation-time constant, not a delivery state

`notifyEligible` SHALL 是创建时即确定的**常量**：`accusation_hit` 为 `true`、`accusation_miss` 为 `false`，语义为「该事件类型是否具备通知资格」。它 SHALL NOT 表示通知已投递，SHALL NOT 被翻转，SHALL NOT 触发对事件文档的任何 update。

通知的实际投递状态与幂等 SHALL 下沉到 `forum_in_app_notifications` 的 `uniq_eventId` 唯一索引（复用 `IForumNotificationWriter` 的 11000 静默跳过范式）。#35 SHALL 维护消费位点、只写余额，SHALL NOT 触碰通知集合或重发通知。

规范与实现注释 SHALL 显式写明「常量标记、非发送状态」双保险，避免测试或 #35 消费方按字面语义跳过发送导致悬赏值奖励通知缺失。

#### Scenario: hit event carries a constant eligibility flag

- **WHEN** 结算批次为成功指认者写入 `accusation_hit` 事件（其 `delta` 为当前悬赏值）
- **THEN** `notifyEligible` SHALL 为 `true`
- **AND** 通知尚未投递时该值 SHALL 不变化，事件文档 SHALL NOT 被 update

#### Scenario: Notification deduplication lives on the notification side

- **WHEN** 批量通知插入遇 `uniq_eventId` 唯一键冲突（11000）
- **THEN** 系统 SHALL 静默跳过该条
- **AND** SHALL NOT 通过修改 `arena_points_events` 的字段来记录投递结果

### Requirement: Reward notification is sent only after reveal succeeds

成功指认者的悬赏值积分站内通知 SHALL 在 #33 当期完整交付（#16 已交付，通知能力不构成阻塞），但 SHALL 仅在揭露批次成功（自白帖发布 + `agent_personas.state=eliminated`）之后**统一批量发送**。系统 SHALL NOT 在指认瞬间发送通知，SHALL NOT 在 `reveal_jobs.state` 变为 `completed` 之前让通知对外可见。

理由：指认即刻发通知会形成时序侧信道，攻击者可用小号逐一指认、以「是否收到奖励通知」枚举 AI 账号。

指认时刻至揭露前，全站通知新增数 SHALL 为 0。F-07 四条断言 SHALL 全部成立：

1. 指认至揭露前全站通知新增 = 0；
2. 揭露后每个成功指认者恰好 1 条悬赏值奖励通知，被指认方与错误指认方收件 = 0；
3. 阈值未达或本周未揭露时 SHALL 不发奖励通知；
4. 同一 `eventId` 重放 SHALL NOT 产生第二条通知。

每位成功指认者 SHALL 各自收到一条通知，通知金额 SHALL 与其积分事件的 `delta`（即该目标当前悬赏值）一致；SHALL NOT 按指认人数分摊或合并发送。

#### Scenario: No notification between accusation and reveal

- **WHEN** 用户完成指认，且本周揭露批次尚未 `completed`
- **THEN** 该用户 SHALL NOT 收到任何指认相关通知
- **AND** `UnreadCount` SHALL NOT 因该指认变化

#### Scenario: Reward notification after successful reveal

- **WHEN** 批次 `completed` 且某用户被判定为成功指认者
- **THEN** 该用户 SHALL 恰好收到 1 条悬赏值积分站内通知
- **AND** 通知金额 SHALL 等于其 `accusation_hit` 事件的 `delta`
- **AND** 被指认方与错误指认方 SHALL 收到 0 条指认相关通知

### Requirement: Silent penalty with no notification

错误指认（目标为 human）的指认者 SHALL 获得一条 `accusation_miss` 事件（`delta = -5`、`notifyEligible = false`），SHALL NOT 收到任何站内通知。扣分明细 SHALL NOT 可被溯源到具体目标；被指认方 SHALL 全程无感知。

积分余额的真实增减、月度衰减、负余额下限（建议 #35 设为 0）、技能商店与 AIQI 跑批 SHALL NOT 属于 #33 范围，SHALL 留待 #35 消费本事件契约。

#### Scenario: Wrong accusation produces a silent penalty event

- **WHEN** 结算批次判定某目标为 human
- **THEN** 其每个指认者 SHALL 获得一条 -5 的 `accusation_miss` 事件
- **AND** SHALL NOT 产生任何站内通知
- **AND** 被指认方 SHALL NOT 收到通知或看到任何站内可见变化

