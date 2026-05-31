## 1. Model.Api：数据模型与索引

- [x] 1.1 `InAppNotificationTypes.ReportAcknowledged`；`ForumReportRecord` 增加 `AcknowledgedAtUtc` / `AcknowledgedBySub`
- [x] 1.2 `ForumMongoSetup`：drop 旧 `ReportId` sparse unique，创建 `(ReportId, Type)` sparse unique 复合索引
- [x] 1.3 `ForumReportListItemDto` 与 `ToListItemDto` 映射 acknowledge 字段

## 2. Model.Api：acknowledge 端点

- [x] 2.1 `ModReportsController` 实现 `POST /api/mod/reports/{reportId}/acknowledge`（pending 校验、版区鉴权、写 acknowledge 字段）
- [x] 2.2 写入 `ReportAcknowledged` 通知（PostTitle 快照、duplicate-key 静默跳过）
- [x] 2.3 `ForumNotificationsController`：`ReportAcknowledged` DTO 映射（系统 actor，与 ReportResolved 一致）

## 3. Model.Api：集成测试

- [x] 3.1 扩展 `ForumReportNotificationTests`（或新建）：acknowledge 写通知、重复幂等、非 pending 400、双通知共存
- [x] 3.2 运行 `dotnet test --filter "FullyQualifiedName~ReportNotification|FullyQualifiedName~ReportAcknowledged"` 通过

## 4. 前端：API 与举报队列

- [x] 4.1 `clients.ts` 新增 `postModReportAcknowledge`
- [x] 4.2 `useModerationReportsQueue` + `ModerationReportsQueueView`：pending 行「已受理」按钮、已受理态展示
- [x] 4.3 `NotificationsView`：`ReportAcknowledged` 文案「您对《…》的举报已受理，正在处理」

## 5. 验证

- [x] 5.1 `dotnet test tests/JIssWeb.Model.Api.Tests` 全量通过
- [x] 5.2 `npm run build` 无编译错误
- [x] 5.3 change-review 对照 delta specs 全部 SHALL 场景
