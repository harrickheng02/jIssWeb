## MODIFIED Requirements

> 账号轮换器（V0.3）落地后，周结算批次完成时多一次「池子补齐」副作用。两阶段提交、单一可见性门禁、prepare 零副作用、commit 全幂等这些既有铁律均不变；本 delta 仅补充轮换触发的位置与门禁关系。

### Requirement: Two-phase settlement with a single external visibility gate

结算批次 SHALL 采用两阶段提交，`reveal_jobs.state=completed` 配合 `publishedAtUtc` SHALL 是**唯一**的对外可见性门禁。

该门禁的作用域 SHALL 限于**本结算批次产出的新增数据**（积分事件、奖励通知、自白帖、身份公开）。其他能力域产生的、不含任何账号身份信息的独立事件（如 `arena-bounty-medal` 的聚合公告）SHALL NOT 被纳入该门禁，也 SHALL NOT 引入第二个对外可见性门禁；此类事件写入即可见，与下述「门禁仅作用于本批次新增数据」自洽。

批次在原子领取成功后、prepare 阶段之前 SHALL 完成当周悬赏值的幂等计算（按账号 × 周键领取计算权、以纯函数覆盖写），供 commit 阶段读取积分金额。该计算失败 SHALL NOT 阻断当周揭露，SHALL 降级为「本周不更新悬赏」并记录日志，积分 SHALL 回落到结算能力原有的基础分值 50。

**prepare 阶段** SHALL：一次性批量只读判定 `accountType` → 生成不可变结算计划（每目标 `verdict` + `accuserId` / `eventId` 清单）→ 落盘 `state=prepared`。此阶段 SHALL 零对外副作用：不写 `arena_points_events`、不更新对外可见的 persona 状态、不发布自白帖、不插入通知、不渲染 AI 角标。批量查询失败时，结算计划 SHALL NOT 落盘，整批 SHALL 不结算、不公开、不发通知，并 SHALL 从断点重跑。

结算计划含提前的 agent 判定，比 `threshold_reached` 更敏感，SHALL 仅系统私有，SHALL NOT 出现在日志、埋点、运营后台、版主审计或任何对外接口中。

**commit 阶段** SHALL 全幂等按序执行：写 `arena_points_events`（私有，`delta` 为该目标当前悬赏值）→ 更新 tally / persona 内部状态 → 生成自白帖（`arena_pending` 私有态）→ `InsertMany` 批量插入悬赏值奖励通知（`uniq_eventId` 幂等，遇 11000 静默跳过）→ **单个 CAS** 置 `reveal_jobs.state=completed` + `publishedAtUtc`。

批次未完成前，外部 SHALL NOT 看到任何部分结果；`completed` 这一次 CAS SHALL 让全部对外数据同时可见。任一步失败续跑 SHALL NOT 重复发通知、扣分或生成自白帖。

**池子补齐（新增）**：批次 `completed` 置位后，结算的调用方 SHALL 触发一次 `agent-rotation` 能力的 `reveal:<weekKey>` 补齐（见 `agent-rotation` 能力）。该副作用 SHALL NOT 纳入本门禁作用域——轮换创建的是 `state=active` 的新 persona，不含任何本批次的对外数据（无帖子、无积分事件、无通知、无身份公开），因此 SHALL NOT 延迟到 `completed` 之前、SHALL NOT 要求第二个门禁，也 SHALL NOT 反向影响 `reveal_jobs` 状态。补齐失败 SHALL NOT 使批次回退为 `prepared` 或重跑。

#### Scenario: Batch account type lookup fails

- **WHEN** prepare 阶段的批量 `accountType` 查询失败
- **THEN** 结算计划 SHALL NOT 落盘
- **AND** 整批 SHALL 不结算、不公开任何 AI 身份、不发任何通知
- **AND** 续跑 SHALL 恰好完成一次结算

#### Scenario: No partial external results before completed

- **WHEN** 批次处于 `prepared` 或 commit 的中间步骤
- **THEN** 自白帖、悬赏值奖励通知、通知未读数、AI 角标与身份公开 SHALL 全部不可见
- **AND** 当 `completed` 被单个 CAS 置位后，上述数据 SHALL 同时对外可见

#### Scenario: Rotation runs after the visibility gate opens

- **WHEN** 某周结算批次 CAS 成功置位 `completed`
- **THEN** 池子补齐 SHALL 在该 CAS 之后发起
- **AND** 补齐 SHALL NOT 修改 `reveal_jobs`、`arena_points_events`、`arena_bounty_states` 或任何已揭露 persona 的状态

#### Scenario: Rotation failure leaves the batch completed

- **WHEN** 池子补齐在结算完成后抛出异常
- **THEN** `reveal_jobs.state` SHALL 保持 `completed`、`publishedAtUtc` SHALL 保持不变
- **AND** 结算结果 SHALL NOT 回滚，续跑 SHALL NOT 重复结算
