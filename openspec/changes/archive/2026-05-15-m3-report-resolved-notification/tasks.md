## 1. 后端：数据模型与索引

- [x] 1.1 在 `InAppNotificationTypes` 中新增常量 `ReportResolved = "ReportResolved"`（`InAppNotificationRecord.cs`）
- [x] 1.2 在 `InAppNotificationRecord` 中新增可空字段 `public string? ReportId { get; set; }`
- [x] 1.3 在 `ForumMongoSetup.EnsureIndexes` 中为 `InAppNotificationRecord.ReportId` 添加 sparse unique index（参照现有 `ReplyId` 索引的写法）

## 2. 后端：通知写入逻辑

- [x] 2.1 在 `ModReportsController` 构造函数中注入 `IMongoCollection<InAppNotificationRecord>` 和 `IMongoCollection<ForumPostRecord>`（与 `_reports` 同一 db）
- [x] 2.2 在 `PatchStatus` 中，`UpdateOneAsync` 成功后，若 `storedStatus` 为 `resolved` 或 `rejected`，查询帖子标题（`_posts.Find(x => x.Id == report.PostId).Project(x => x.Title).FirstOrDefaultAsync()`；查不到则 `""`）
- [x] 2.3 构造 `InAppNotificationRecord`（`Type=ReportResolved`、`RecipientSubId=report.ReporterSub`、`PostId=report.PostId`、`PostTitle`、`ActorSubId=""`、`ReportId=report.Id`、`CreatedAtUtc=DateTime.UtcNow`），调用 `_notifications.InsertOneAsync`；捕获 `MongoWriteException`（`DuplicateKey`）静默跳过
- [x] 2.4 确认 `status = pending`（重开）不触发任何通知写入路径

## 3. 后端：DTO 映射

- [x] 3.1 在 `ForumNotificationsController.MapDto` 中对 `Type == InAppNotificationTypes.ReportResolved` 特判：`ActorDisplayName` 返回固定值 `"系统"`，不走 `_authorNames.ResolveAsync`（避免空字符串 sub 查询）
- [x] 3.2 确认 `ActorId` 字段在 `ReportResolved` 通知 DTO 中为空字符串（不影响前端 key）

## 4. 后端：集成测试

- [x] 4.1 在 `backend/tests/JIssWeb.Model.Api.Tests/` 新增或扩展举报通知测试 fixture，覆盖：`resolved` 触发通知写入（`RecipientSubId`、`Type`、`ReportId` 断言）
- [x] 4.2 集成测试覆盖：`rejected` 触发通知写入
- [x] 4.3 集成测试覆盖：幂等场景——举报重开后再次关闭，`forum_in_app_notifications` 中 `ReportId` 对应记录仍只有一条
- [x] 4.4 集成测试覆盖：`pending`（重开）操作不产生通知记录
- [x] 4.5 集成测试覆盖：帖子不存在时 `PostTitle` 存储为空字符串，通知写入仍成功
- [x] 4.6 运行 `dotnet test tests/JIssWeb.Model.Api.Tests` 确认全部通过

## 5. 前端：通知列表渲染

- [x] 5.1 在通知列表组件（`frontend/src/` 中相关 Vue 文件）对 `type === "ReportResolved"` 的通知项特判文案：显示「您对《{{ postTitle || "内容已移除" }}》的举报已处理」
- [x] 5.2 `ReportResolved` 通知的 actor 区域显示 `actorDisplayName`（后端已返回"系统"），不显示头像或用户链接
- [x] 5.3 深链逻辑：点击 `ReportResolved` 通知跳转到 `PostId` 对应帖子详情页（`PostId` 为空或帖子不存在时降级为不跳转或显示「内容已移除」）
- [x] 5.4 确认通知项样式遵循 `forum-tokens.css` 变量，无硬编码颜色或间距
- [x] 5.5 运行 `npm run build`（类型检查 + Vite build）确认前端无编译错误
