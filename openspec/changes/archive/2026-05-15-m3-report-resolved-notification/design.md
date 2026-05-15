## Context

举报通知是跨两个已有模型的副作用扩展：`forum_reports`（举报工单）和 `forum_in_app_notifications`（站内通知）。两者均位于 `JIssWeb.Model.Api`，共享同一 MongoDB 数据库，无需跨服务调用。

当前状态：
- `InAppNotificationRecord` 只有 `ReplyToPost` 一种类型，幂等键为 `ReplyId`（sparse unique index on `forum_in_app_notifications`）。
- `ModReportsController.PatchStatus` 在 status 写入成功后直接返回，无通知副作用。
- `ForumNotificationsController.MapDto` 尚未处理系统通知（ActorSubId 为空）的显示。

## Goals / Non-Goals

**Goals:**
- 举报状态变为 `resolved` 或 `rejected` 时，向举报人写一条 `ReportResolved` 类型通知。
- 通知可幂等写入：重开再关闭同一举报不产生第二条通知。
- 前端通知列表正确渲染举报结案文案，深链至帖子。
- 后端集成测试覆盖通知写入和幂等保护。

**Non-Goals:**
- 邮件、短信等站外通知渠道。
- 举报进度实时查询 API。
- 向举报人公开处理结论（resolved vs rejected）。
- 举报处理 SLA 计量。
- `pending` 状态变更时发送通知（只在终态触发）。

## Decisions

### 决策 1：`ReportId` 作为幂等键，与 `ReplyId` 并列

**选择**：在 `InAppNotificationRecord` 新增 `string? ReportId`，`ForumMongoSetup` 为其创建 sparse unique index。

**理由**：与现有 `ReplyId` 幂等键的设计一致；sparse index 使非举报通知行不受影响；一个举报 ID 对应至多一条通知行，无论举报经历多少次重开再关闭。

**备选**：用 `(RecipientSubId, Type, targetId)` 组合索引——但不能区分同一帖子被同一人多次举报的不同工单（理论上已被 pending 唯一索引防止，但不如显式 ReportId 语义清晰）。

### 决策 2：`resolved` 和 `rejected` 均触发同一类型通知，文案不区分结论

**选择**：两个终态统一写 `ReportResolved` 类型，前端文案固定为「您对《XXX》的举报已处理」。

**理由**：避免举报人因看到"已驳回"而产生不满或反复举报；符合平台治理的最小信息披露原则。

**备选**：区分 `ReportRejected` 和 `ReportResolved` 两种类型——增加复杂度，且对产品目标无收益。

### 决策 3：通知写入在 `PatchStatus` 中内联，不引入事件总线

**选择**：`UpdateOneAsync` 成功后，直接在同一请求调用链中 `InsertOneAsync` 写通知；捕获 MongoWriteException（DuplicateKey）静默跳过。

**理由**：当前无消息队列基础设施；内联写入实现最简单，对端到端延迟影响极小（同数据库同进程）；若通知写入失败（非幂等冲突），不应回滚举报 status 变更（两者是不同关注点），因此不包裹事务。

**备选**：MongoDB 事务保证原子性——事务需 replica set，本地 dev 配置和测试复杂度上升，通知丢失的影响远低于举报状态写失败，不值得引入。

### 决策 4：`PostTitle` 写通知时查帖子，帖已删则留空

**选择**：写通知前查一次 `forum_posts` 获取 `Title`；帖已删或查不到则 `PostTitle = ""`。

**理由**：通知落库后帖子可能被后续删除，存储写入时的标题快照比运行时再查更稳定；留空（前端显示「内容已移除」）优于查不到时报错阻断通知写入。

### 决策 5：`ActorSubId = ""`，`ActorDisplayName` 前端返回"系统"

**选择**：通知记录 `ActorSubId` 存空字符串；`ForumNotificationsController.MapDto` 对 `ReportResolved` 类型特判，`ActorDisplayName` 返回固定值"系统"（不走 `ResolveAsync`）。

**理由**：不暴露版主身份；空字符串在 `ResolveAsync` 中会触发无效查询，特判更干净；对 `ReplyToPost` 类型无影响。

## Risks / Trade-offs

- **通知与举报 status 不原子** → `PatchStatus` 写 status 成功但通知写入失败（非幂等冲突）时，举报人不会收到通知。由于通知是补充性体验，不做回滚；可通过后续监控或重试任务补偿。该风险等级低。
- **`PostTitle` 快照在写入时可能已过期** → 帖子被版主在举报处理前修改标题；通知中的标题与当前帖子不一致。属于可接受的最终一致。
- **`ReportId` sparse index 不阻止同一举报人对同一目标在不同工单上的多次通知** → 由于 `forum_reports` 的 pending 唯一索引已阻止对同一目标的并发 pending 工单，实际上同一目标最多存在一个已结案工单在保留期内，风险极低。

## Migration Plan

1. 部署新代码（无 schema migration，`ReportId` 字段 nullable，旧通知行不受影响）。
2. 服务启动时 `ForumMongoSetup.EnsureIndexes` 自动创建 `ReportId` sparse unique index（幂等）。
3. 无需数据回填：历史举报无需补通知。
4. 回滚：删除新代码重新部署；sparse index 保留无副作用，或手动 dropIndex。

## Open Questions

- 若 `PostTitle` 超长（> 200 字符），通知文案是否需要截断？建议前端截断显示，后端存原始值。（实现时确认前端 line-clamp 策略）
