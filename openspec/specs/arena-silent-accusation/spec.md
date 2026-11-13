## Purpose

本规范为 `arena-silent-accusation` 能力的归档基线，汇总历次 OpenSpec 变更产出的 5 条需求及其验收场景（Scenario），作为后续变更的 MODIFIED / ADDED delta 的合并目标。

## Requirements

### Requirement: Silent accusation endpoint with a uniform response

Model 服务 SHALL 提供 `POST /api/arena/accusations`，请求体含 `targetUserId`，仅对已登录用户开放。指认记录 SHALL 仅对系统可见，不向任何用户（含指认者本人、被指认方、版主、运营账号）公开。

所有业务分支 SHALL 返回**字节级一致**的成功响应（含响应头）：`200 {code:0,message:"ok",data:{}}`。业务分支至少包括：首次指认成功、7 天内重复指认、目标不存在、目标不可指认、参数校验失败、限流命中、鉴权失败、内部异常。系统 SHALL NOT 通过状态码、响应体字段、响应头、耗时或错误信息区分这些分支。

系统 SHALL NOT 提供指认记录的查询端点（GET）、「我的指认」列表、指认结果反馈、待揭露数量、红点、倒计时或任何等价能力。

#### Scenario: First accusation is recorded privately

- **WHEN** 已登录用户提交合法的 `targetUserId`
- **THEN** 系统 SHALL 写入一条 `status=pending` 的 `arena_accusations` 记录与一条 `arena_accusation_dedupe` 记录
- **AND** SHALL 对 `arena_target_tallies` 执行 `$inc` CAS
- **AND** SHALL 返回统一成功响应，不暴露目标身份、判定结果或记录条数

#### Scenario: Duplicate and invalid branches are indistinguishable

- **WHEN** 请求命中 7 天重复、目标不存在、参数错误、限流或内部异常中的任意一种
- **THEN** 系统 SHALL 返回与首次成功指认字节级一致的 `200` 响应
- **AND** SHALL NOT 在响应体、响应头、日志或审计 feed 中留下可区分的业务分支痕迹

### Requirement: Seven-day deduplication with decoupled record lifetimes

验收①「同一用户对同一目标 7 天内只能指认一次」SHALL 由独立的 `arena_accusation_dedupe` 集合承担：唯一索引 `{accuserId,targetUserId}` + TTL 7 天。去重 SHALL 通过原子插入实现，遇唯一键冲突（11000）静默跳过。

`arena_accusations` SHALL NOT 设置 7 天 TTL。它是待结算业务记录，SHALL 保留至结算完成并经审计保留期后才归档清理。原因是统一结算需扫描本周全部指认，周一指认至下周五结算最长约 13 天 22 小时，超过 7 天；若业务记录带 7 天 TTL 会被物理删除，导致漏结算与漏发积分事件。

归档条件 SHALL 为 `settledAtUtc` 存在且距今 ≥14 天（实现可取 21 天留余量）。归档任务 SHALL NOT 早于结算完成被调度。

#### Scenario: Monday accusation survives until the following Friday settlement

- **WHEN** 某用户在周一提交指认，结算批次在下一个周五 22:00 运行
- **THEN** 该 `arena_accusations` 记录 SHALL 仍然存在且 `status=pending`
- **AND** SHALL 被该批次结算，产出对应的 hit / miss 事件
- **AND** SHALL NOT 因 TTL 被物理删除

#### Scenario: Re-accusation is allowed on day eight

- **WHEN** 同一用户对同一目标在首次指认后第 8 天再次提交
- **THEN** `arena_accusation_dedupe` 的 TTL 已清除原去重记录
- **AND** 系统 SHALL 允许本次指认并写入新的记录

### Requirement: Zero identity resolution on the accusation write path

指认写路径 SHALL 严格限定为：`arena_accusation_dedupe` 原子插入 → `arena_accusations` 写 `status=pending` → `arena_target_tallies` 的 `$inc` CAS → 统一 200。

该路径 SHALL NOT 调用 User.Api 或任何跨服务身份查询，SHALL NOT 读取或推断目标的 `accountType`，SHALL NOT 写入 `arena_points_events`，SHALL NOT 发送任何站内通知，SHALL NOT 判定 hit / miss，SHALL NOT 将任何目标标记为「待揭露」。

`tallies` 的 `$inc` SHALL 对所有目标累加，不因目标是否为 agent 而分流。目标类型的批量判定 SHALL 只在周五结算批次进行。

#### Scenario: Accusation moment emits no points events or notifications

- **WHEN** 用户提交一次合法指认
- **THEN** `arena_points_events` SHALL 无新增文档
- **AND** `forum_in_app_notifications` SHALL 无新增文档
- **AND** SHALL 无任何跨服务 `accountType` 查询
- **AND** 审计 feed SHALL 无指认记录

### Requirement: Threshold reached is an internal counting state only

`arena_target_tallies` 的状态机 SHALL 为 `active → threshold_reached → pending_reveal | void_human`。阈值默认 3 人且 SHALL 可配置（配置文件 / 环境变量承载，本期不建后台 UI）。

`threshold_reached` SHALL 仅表示「计数达到阈值」，SHALL NOT 表示或暗示目标已被确认为 agent。系统 SHALL NOT 将 `threshold_reached` 作为 AI 身份信号出现在任何对外接口、日志、前端埋点、站内通知、运营后台或版主审计中。`arena_target_tallies` SHALL 无对外路由；运营后台白名单字段 SHALL 仅限 `weekKey` / `jobStatus` / `processedCount`。

`pending_reveal` SHALL 只在结算批次批量判定目标为 agent 后设置。

#### Scenario: Threshold reached does not leak AI identity

- **WHEN** 某目标累计指认达到阈值
- **THEN** tally 状态 SHALL 变为 `threshold_reached`
- **AND** 任何对外接口、日志、埋点、通知、运营后台与版主审计 SHALL NOT 出现该状态或可推断目标为 AI 的信息
- **AND** SHALL NOT 因此触发任何揭露动作

### Requirement: Side-channel prohibitions on target identity

`targetUserId` 及其任何哈希 SHALL NOT 出现在 API 响应、站内通知、积分明细、服务端日志、前端埋点或 BFF 字段投影中。前端埋点白名单 SHALL 仅包含 `ts` / `accuserIdHash` / `weekKey` 与聚合计数。

版主审计 feed SHALL 显式排除指认事件；版主与运营账号 SHALL NOT 在揭露前看到指认记录、待揭露记录或结算计划。研究者仅 SHALL 在揭露完成后查询已归档数据。

三道上线闸门 SHALL 全部通过：human 目标零感知；版主 / 运营账号看不到揭露前的指认与待揭露记录；指认后至揭露前零通知。

#### Scenario: Moderator account sees nothing before reveal

- **WHEN** 版主或运营账号在揭露前查询治理、审计、通知或内容视图
- **THEN** SHALL NOT 出现任何指认记录、待揭露目标、结算计划或目标身份线索
- **AND** 该账号作为人类玩家时 SHALL 与普通用户拥有完全相同的可见性

#### Scenario: Frontend telemetry carries no target identifier

- **WHEN** 前端上报指认相关埋点
- **THEN** 字段 SHALL 仅为 `ts` / `accuserIdHash` / `weekKey` / 聚合计数
- **AND** SHALL NOT 包含 `targetUserId`、`targetUserIdHash` 或任何可关联枚举目标的字段
