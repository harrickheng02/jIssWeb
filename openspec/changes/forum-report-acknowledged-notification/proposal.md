## Why

Issue #19 change-B：举报人在提交后仅有结案通知（Issue #21），缺少「已受理」中间反馈。补一条幂等站内通知，与结案通知形成两节点闭环，且不向举报人公开处理结论或版主身份。

## What Changes

- 新增通知类型 `ReportAcknowledged`：`POST /api/mod/reports/{reportId}/acknowledge` 触发，举报工单 **status 仍为 `pending`**。
- 幂等：将 `forum_in_app_notifications` 上 `ReportId` 唯一索引改为 **`(ReportId, Type)`** 复合稀疏唯一，使同一举报可各存一条「已受理」与「已结案」通知。
- 前端举报队列增加「标记已受理」操作；通知列表渲染「您对《…》的举报已受理，正在处理」类文案。
- **不**修改 `PATCH` 中 `acknowledged`→`resolved` 的既有别名语义（避免破坏存量与结案通知测试）。

## Capabilities

### New Capabilities

- `report-acknowledged-notification`：举报人「已受理」站内通知的类型、触发点、幂等与前端文案。

### Modified Capabilities

- `forum-report-api`：新增 `POST .../acknowledge` 端点；`ForumReportRecord` 可选 `AcknowledgedAtUtc` / `AcknowledgedBySub` 供队列展示。
- `in-app-notifications`：`ReportAcknowledged` 类型与列表 DTO 映射；通知索引契约调整。
- `forum-report-moderation-ui`：举报队列「已受理」按钮与状态展示（仍属三类工单终态筛选，已受理为 pending 子态）。

## Impact

- **Model.Api**：`ModReportsController`、Mongo 索引迁移、`ForumNotificationsController`、集成测试。
- **Frontend**：`clients.ts`、`ModerationReportsQueueView`、`NotificationsView`；遵循 `forum-tokens.css`。
- **依赖**：Issue #4/#16/#21 closed；父 Issue #19 change-A merged。

## 非目标

邮件/短信；举报人进度查询 API；公开 resolved/rejected 结论；版主身份披露；SLA/指派；修改 `PATCH {status: acknowledged}` 为独立进行中状态（本 change 用专用 acknowledge 端点）。
