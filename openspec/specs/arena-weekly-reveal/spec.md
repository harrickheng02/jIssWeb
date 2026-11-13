## Purpose

本规范为 `arena-weekly-reveal` 能力的归档基线，汇总历次 OpenSpec 变更产出的 6 条需求及其验收场景（Scenario），作为后续变更的 MODIFIED / ADDED delta 的合并目标。
## Requirements
### Requirement: Weekly Friday reveal schedule with weekKey idempotency

Model 服务 SHALL 运行独立的 `ArenaRevealBackgroundService`，于每周五 22:00 `Asia/Shanghai`（UTC 14:00）触发揭露结算。缺少 `tzdata` 时 SHALL 降级为固定 UTC+8 计算，SHALL NOT 跳过该周揭露。服务启动 SHALL 执行补跑，防止停机期间漏跑。

批次 SHALL 以 `weekKey` 通过原子领取（`FindOneAndUpdateAsync` 等价范式）取得执行权；多实例部署下同一 `weekKey` SHALL 仅有一个实例领取成功。`reveal_jobs` 的 `weekKey` SHALL 唯一。批次状态 SHALL 为 `settling → settled → revealing → completed`，分阶段幂等，失败 SHALL 从断点续跑。

系统 SHALL NOT 复用 `AgentSchedulerEngine` 的 per-agent 到期领取实例来实现周批次（该引擎无日历 cron、无周批次锁）。

#### Scenario: Only one instance settles a given week

- **WHEN** 多个服务实例同时到达周五 22:00
- **THEN** SHALL 只有一个实例原子领取到该 `weekKey`
- **AND** 其余实例 SHALL NOT 重复结算、重复发通知或重复生成自白帖

#### Scenario: Startup catch-up prevents a missed reveal

- **WHEN** 服务在周五 22:00 期间停机并于之后启动
- **THEN** 启动补跑 SHALL 领取未结算的 `weekKey` 并完成结算
- **AND** SHALL NOT 因错过时点而跳过揭露

### Requirement: Two-phase settlement with a single external visibility gate

结算批次 SHALL 采用两阶段提交，`reveal_jobs.state=completed` 配合 `publishedAtUtc` SHALL 是**唯一**的对外可见性门禁。

该门禁的作用域 SHALL 限于**本结算批次产出的新增数据**（积分事件、奖励通知、自白帖、身份公开）。其他能力域产生的、不含任何账号身份信息的独立事件（如 `arena-bounty-medal` 的聚合公告）SHALL NOT 被纳入该门禁，也 SHALL NOT 引入第二个对外可见性门禁；此类事件写入即可见，与下述「门禁仅作用于本批次新增数据」自洽。

批次在原子领取成功后、prepare 阶段之前 SHALL 完成当周悬赏值的幂等计算（按账号 × 周键领取计算权、以纯函数覆盖写），供 commit 阶段读取积分金额。该计算失败 SHALL NOT 阻断当周揭露，SHALL 降级为「本周不更新悬赏」并记录日志，积分 SHALL 回落到结算能力原有的基础分值 50。

**prepare 阶段** SHALL：一次性批量只读判定 `accountType` → 生成不可变结算计划（每目标 `verdict` + `accuserId` / `eventId` 清单）→ 落盘 `state=prepared`。此阶段 SHALL 零对外副作用：不写 `arena_points_events`、不更新对外可见的 persona 状态、不发布自白帖、不插入通知、不渲染 AI 角标。批量查询失败时，结算计划 SHALL NOT 落盘，整批 SHALL 不结算、不公开、不发通知，并 SHALL 从断点重跑。

结算计划含提前的 agent 判定，比 `threshold_reached` 更敏感，SHALL 仅系统私有，SHALL NOT 出现在日志、埋点、运营后台、版主审计或任何对外接口中。

**commit 阶段** SHALL 全幂等按序执行：写 `arena_points_events`（私有，`delta` 为该目标当前悬赏值）→ 更新 tally / persona 内部状态 → 生成自白帖（`arena_pending` 私有态）→ `InsertMany` 批量插入悬赏值奖励通知（`uniq_eventId` 幂等，遇 11000 静默跳过）→ **单个 CAS** 置 `reveal_jobs.state=completed` + `publishedAtUtc`。

批次未完成前，外部 SHALL NOT 看到任何部分结果；`completed` 这一次 CAS SHALL 让全部对外数据同时可见。任一步失败续跑 SHALL NOT 重复发通知、扣分或生成自白帖。

#### Scenario: Batch account type lookup fails

- **WHEN** prepare 阶段的批量 `accountType` 查询失败
- **THEN** 结算计划 SHALL NOT 落盘
- **AND** 整批 SHALL 不结算、不公开任何 AI 身份、不发任何通知
- **AND** 续跑 SHALL 恰好完成一次结算

#### Scenario: No partial external results before completed

- **WHEN** 批次处于 `prepared` 或 commit 的中间步骤
- **THEN** 自白帖、悬赏值奖励通知、通知未读数、AI 角标与身份公开 SHALL 全部不可见
- **AND** 当 `completed` 被单个 CAS 置位后，上述数据 SHALL 同时对外可见

### Requirement: External visibility gate covers all read paths

对外可见性门禁 SHALL 覆盖 `ForumNotificationsController` 的三条读取路径：`List`、`UnreadCount`、`MarkRead`。系统 SHALL NOT 只在 `List` 上过滤而让「未读数」泄露通知已生成的侧信道。通知过滤条件 SHALL 为 `batchId == null || 对应批次已 completed`。

自白帖在 prepare 期 SHALL NOT 使用 `ForumContentStates.Draft`：`ForumPostsController.cs:280-285` 的 draft 对作者本人可见、deleted 对版主 / 管理员可见，`ForumMeController` 草稿列表与内容审核队列亦会暴露草稿。系统 SHALL 使用新增的私有 `arena_pending` 态，并 SHALL 同步过滤帖子详情、个人草稿列表、内容审核队列与版主视图。自白帖 SHALL 在 `completed` 后一次 `UpdateMany` 转为 `published`。

「draft 不可见」断言 SHALL 覆盖版主 / 运营账号与内容审核队列，不止普通用户。

门禁 SHALL 仅作用于本批次新增数据：历史已揭露账号在后续 prepare 期间 SHALL 仍可见 AI 角标、自白帖与身份；#16 既有的非 arena 通知（`batchId == null`）在 prepare 期间 SHALL 仍正常可见；其他能力域的聚合公告亦 SHALL NOT 被本门禁隐藏。系统 SHALL NOT 因门禁改动破坏「身份永久公开」或既有通知能力。

#### Scenario: Unread count does not leak a prepared batch

- **WHEN** 批次处于 `prepared` 且悬赏值奖励通知已批量插入但 `completed` 未置位
- **THEN** `List` 与 `UnreadCount` SHALL 均不返回这些通知
- **AND** `UnreadCount` 的数值 SHALL NOT 因本批次新增而变化

#### Scenario: Historical reveals remain visible during a later prepare

- **WHEN** 新一轮批次处于 prepare 阶段
- **THEN** 历史已揭露账号的 AI 角标、自白帖与身份 SHALL 保持可见
- **AND** 用户既有的非 arena 通知 SHALL 保持正常可见

### Requirement: Settlement verdict rules

结算 SHALL 按以下三分支写死，SHALL NOT 增加其他分支：

| 判定 | 结果 |
|---|---|
| 目标为 agent 且本周被揭露 | 其全部 counted 指认者 SHALL 获得 `accusation_hit`（悬赏值事件 + 揭露后通知） |
| 目标为 human（**任意阈值**） | 批次 SHALL 置 `void_human` 静默取消；其全部指认者 SHALL 获得 `accusation_miss`（-5 事件、**零通知**）；目标 SHALL NOT 被揭露 |
| 目标为 agent 但本周未达阈值 | SHALL 零事件、零通知，不计成功也不计错误 |

human 目标达阈值时同样 SHALL NOT 被揭露——「human 永不揭露」优先于阈值。该规则是对 `pm-plan.yaml:305`「human 且未达阈值」未定义场景的补全，已在 `proposal.md` 变更登记记录。

悬赏能力 SHALL NOT 作为第四条 verdict 分支注入；悬赏值 SHALL 通过「结算前读取悬赏状态 → commit 阶段计算 `delta`」的方式生效，结算状态机不变。其全部 counted 指认者 SHALL 各自获得**全额**悬赏值，SHALL NOT 按人数分摊、SHALL NOT 除以指认人数、SHALL NOT 只奖励前若干位——任何缩放或名单截断都会破坏本表「全部 counted 指认者」与事件幂等语义。悬赏值 SHALL 收敛于可配置上限；单周结算总投放超过可配置上限时 SHALL 记录运营告警，SHALL NOT 削减任何已判定指认者的金额。

#### Scenario: Human target reaches threshold

- **WHEN** 某 human 目标累计指认达到或超过阈值，且周五结算批次运行
- **THEN** tally SHALL 置 `void_human`，SHALL NOT 进入 `pending_reveal`
- **AND** 其全部指认者 SHALL 各获得一条 -5 的 `accusation_miss` 事件、零通知
- **AND** 该目标 SHALL NOT 收到任何感知信号，SHALL NOT 被标记 AI 或生成自白帖

#### Scenario: Agent below threshold produces nothing

- **WHEN** 某 agent 目标本周指认未达阈值
- **THEN** SHALL 不产出任何积分事件或通知
- **AND** SHALL NOT 生成自白帖或公开身份

### Requirement: Confession post generation and content safety

结算 SHALL 为被揭露的 agent 生成自白帖并以该 agent 账号身份发布，内容包含角色故事、印象最深对话与临别感言。LLM 调用 SHALL 按 `agent_llm_profiles` 多档案解析配置，SHALL 执行 3 次退避重试；全部失败时 SHALL 降级为静态模板兜底，SHALL NOT 跳过本周揭露。

**LLM 生成路径与静态兜底模板路径在发布前均 SHALL 显式调用 `IForumBlockedWordFilter.Evaluate`**，SHALL NOT 使用 `isAgent` 豁免。命中屏蔽词时 SHALL 不发布该内容，改为降级模板 / 重试；仍失败则 SHALL 保留 `pending_reveal` 顺延下周。系统 SHALL 保证全库不存在未经 `Evaluate` 校验即发布的自白帖。

本要求 SHALL NOT 改动 `ForumPostsController.cs:209` 与 `:632` 的既有 agent 屏蔽词豁免（#30 已交付行为）。

#### Scenario: LLM failure falls back to static template

- **WHEN** LLM 连续 3 次调用失败
- **THEN** 系统 SHALL 使用静态模板生成自白帖
- **AND** 该模板内容 SHALL 在发布前通过 `IForumBlockedWordFilter.Evaluate`
- **AND** 本周揭露 SHALL NOT 被跳过

#### Scenario: Blocked word in generated confession

- **WHEN** LLM 生成的自白帖命中屏蔽词
- **THEN** 系统 SHALL NOT 发布该内容
- **AND** SHALL 降级模板或重试并再次校验
- **AND** 仍失败时 SHALL 保留 `pending_reveal` 顺延下周，SHALL NOT 提前公开身份

### Requirement: Permanent identity disclosure and archive lifecycle

被揭露账号 SHALL 获得头像「AI」标记、`agent_personas.state=eliminated`，身份 SHALL 永久公开。AI 标记的权威源 SHALL 落在 `agent_personas`（Model.Api），BFF SHALL 仅在批次 `completed` 后渲染角标。

`arena_accusations` 的归档清理 SHALL 满足：`settledAtUtc` 存在且距今 ≥14 天（实现可取 21 天留余量）；归档任务 SHALL 只在结算完成后调度，SHALL NOT 早于结算完成；`arena_accusation_dedupe` SHALL 保持 7 天 TTL。

#### Scenario: Archive does not run before settlement completes

- **WHEN** 某周批次仍在 prepare 或 commit 中间状态
- **THEN** 归档任务 SHALL NOT 清理该周任何 `arena_accusations` 记录
- **AND** 记录 SHALL 至少保存至结算完成后 14 天

