## Context

Issue #21 已在举报 `PATCH` 结案（`resolved`/`rejected`）时写入 `ReportResolved` 通知。当前 `forum_in_app_notifications.ReportId` 为 sparse unique，同一举报只能有一条带 `ReportId` 的通知。Issue #19 change-B 需在**不破坏**既有 `PATCH {status: acknowledged}`→`resolved` 别名与三类工单筛选的前提下，增加「已受理」中间反馈。

举报人仍不应看到版主身份或 resolved/rejected 细节。

## Goals / Non-Goals

**Goals:**

- 版主/管理员可对 `pending` 举报执行「已受理」，向举报人发送幂等 `ReportAcknowledged` 通知。
- 同一举报可先后拥有「已受理」与「已结案」两条通知（两节点闭环）。
- 举报队列可见已受理标记；工单 `status` 仍为 `pending` 直至 `PATCH` 结案。

**Non-Goals:**

- 第四类终态 `acknowledged`；修改 `PATCH` 别名语义。
- 邮件/短信、SLA、进度 API、公开裁决结论。

## Decisions

### 1. 专用 `POST /api/mod/reports/{reportId}/acknowledge`（而非扩展 PATCH）

**选择**：独立端点，仅写 `AcknowledgedAtUtc`/`AcknowledgedBySub` 并触发通知；**不**改变 `status`。

**理由**：现有 `acknowledged` PATCH 别名已映射为结案 `resolved` 并触发 `ReportResolved`；改为进行中态会破坏存量测试与 spec。专用端点语义清晰、迁移面小。

**备选**：将 PATCH `acknowledged` 改为进行中态 — 拒绝（BREAKING + 与 legacy Mongo `acknowledged` 存量冲突）。

### 2. 幂等键：复合 sparse unique `(ReportId, Type)`

**选择**：`ForumMongoSetup`  drop 旧 `ReportId` unique，创建 `{ ReportId: 1, Type: 1 }` sparse unique。

**理由**：`ReportResolved` 与 `ReportAcknowledged` 可共存；同类型重复写入仍 duplicate-key skip。

**备选**：仅 `ReportId` unique + 单条合并通知 — 拒绝（无法表达两节点）。

### 3. 已受理后工单仍为 pending

**选择**：列表 DTO 增加 `acknowledgedAtUtc` / `acknowledgedBySub`（nullable）；`status` 保持 `pending`。

**理由**：默认「待处理」筛选仍有效；版主通过字段区分「未看过」与「已受理待结案」。

### 4. 通知文案与 actor

与 `ReportResolved` 一致：`ActorSubId=""`，`ActorDisplayName="系统"`；前端文案「您对《PostTitle》的举报已受理，正在处理」；`PostTitle` 写时快照。

## Risks / Trade-offs

- **[Risk] 索引迁移失败** → Mitigation：`ForumMongoSetup` 启动时 drop 旧 index by name 再 create；集成测试覆盖双通知插入。
- **[Risk] 版主重复点受理** → Mitigation：duplicate-key 静默跳过；UI 禁用或显示已受理态。
- **[Trade-off] pending 队列仍含已受理单** → 通过行内「已受理」标记缓解；不做单独 filter（非目标）。

## Migration Plan

1. 部署 Model.Api：`ForumMongoSetup` 更新索引；`ForumReportRecord` 新字段向后兼容（旧文档字段为空）。
2. 部署前端：队列按钮 + 通知列表文案。
3. 回滚：回退代码；Mongo 复合索引可保留（旧代码仅写 ReportResolved，仍兼容）。

## Open Questions

- 无。实现阶段若产品希望「已受理」从 pending 默认列表隐藏，可另开 change 加 `status` 过滤参数。
