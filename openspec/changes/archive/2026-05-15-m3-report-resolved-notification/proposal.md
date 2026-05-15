## Why

举报人在提交举报后缺乏闭环反馈，无法感知自己的举报已被版主处理。在现有站内通知基础设施（Issue #16 已交付）和举报数据模型（Issue #4 已交付）之上，为举报人新增一条系统通知，完成举报工作流的最后一公里。本改动对应 Issue #21，从属于 M3 里程碑（父 Issue #19）。

## What Changes

- 后端新增通知类型 `ReportResolved`：`InAppNotificationTypes` 中增加常量，`InAppNotificationRecord` 增加可空字段 `ReportId`（作为幂等键）。
- `ForumMongoSetup` 为 `InAppNotificationRecord.ReportId` 增加 sparse unique index，防止重复通知。
- `ModReportsController.PatchStatus`（`PATCH /api/mod/reports/{id}`）写入 status 成功后，若新 status 为 `resolved` 或 `rejected`，向 `ForumReportRecord.ReporterSub` 发送一条 `ReportResolved` 通知；`ActorSubId` 写空字符串（系统行为，不暴露处理者）；`PostTitle` 在写通知时查帖子标题填入，帖已删则留空。
- 前端 `ForumNotificationsController.MapDto` 中对 `ReportResolved` 类型特判：`ActorDisplayName` 返回空或"系统"，文案统一为「您对《PostTitle》的举报已处理」；深链指向 `PostId`；帖子不存在时显示「内容已移除」。
- 重开场景（举报被重开再次关闭）由 `ReportId` 唯一索引静默跳过，不产生重复通知。

## Capabilities

### New Capabilities

- `report-resolved-notification`：举报结案后向举报人发送系统站内通知，包含通知类型定义、幂等写入规则、后端触发点与前端渲染文案。

### Modified Capabilities

- `forum-report-api`：`PATCH /api/mod/reports/{id}` 在 status 变更为终态（`resolved`/`rejected`）后触发写通知副作用；该副作用通过 `ReportId` 幂等保护。
- `in-app-notifications`：新增 `ReportResolved` 通知事件类型，扩展通知列表 DTO 映射规则；现有 `ReplyToPost` 行为不受影响。

## Impact

- **服务边界**：仅影响 `JIssWeb.Model.Api`（后端）与 SPA（前端通知列表）。
- **数据库**：`in_app_notifications` 集合新增 `reportId` 字段和 sparse unique index。
- **依赖**：Issue #4 closed（`ForumReportRecord` 数据模型）；Issue #16 closed（`InAppNotificationRecord`、`ForumNotificationsController`、通知列表 UI 已就绪）。
- **非目标**：邮件 / 短信通知；举报进度查询 API；处理 SLA 指标；处理结论（resolved vs rejected）对举报人公开；举报人查看完整裁决详情。
- **前端 UI**：遵循 `forum-tokens.css` CSS 变量约束，不引入硬编码颜色或间距。
