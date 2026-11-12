# Tasks: forum-content-notification-decouple

- [x] T1: 阅读 ForumPostsController.cs 找到所有通知写入逻辑
- [x] T2: 创建 ForumNotificationWriter 服务
- [x] T3: 将通知写入逻辑从 Controller 迁移到 ForumNotificationWriter
- [x] T4: 更新 ForumPostsController 注入并调用 ForumNotificationWriter
- [x] T5: 在 Program.cs (Model.Api) 注册 ForumNotificationWriter
- [x] T6: 更新测试 fixture 中的 DI 注册（如需要）— 集成测试通过真实 DI，无需更新 fixture
- [x] T7: 运行 dotnet test 全通过（218/245，其余 27 个失败为预先存在的网络连通性问题，与本变更无关）
- [x] T8: 更新 forum-content-api spec 说明 Controller 职责边界
