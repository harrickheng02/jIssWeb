## 范围

演示 `scripts/github-sync/pm-plan.yaml` **Issue #4 — 论坛举报与处理最小闭环**：

- 用户提交举报；
- Mongo `forum_reports` 产生待处理记录；
- 版主/管理员在「举报队列」将每条举报切换为 **待处理 / 已驳回 / 已处置**（`PATCH` 仅传 `status`）；契约要求 **`PATCH`** 仅持久化 **`forum_reports`**（根 **`openspec/specs/forum-report-api`**）。

前置：可用的 user-service JWT（登录）、Model.Api、`forum_general`/`forum_tech` 等版区下有帖与可选回复。

## A. 用户举报

1. 使用普通会员账号登录，打开任一帖子详情。
2. 点击主帖旁的「举报帖子」，填写可选说明（≤500 字），提交。
3. 期望：Toast 提示成功；同一帖子对同一账号重复举报待处理前应提示「已有待处理」类 409。
4. 在回复列表对某条回复点「举报」并提交，期望成功。
5. 未登录时点举报入口：应跳转登录。

## B. 版主/管理员队列

6. 使用带 `forumRole` 为 `moderator` 或 `admin` 的账号登录（JWT 与原治理能力一致）。
7. 打开 **`/moderation`** 治理说明页，点击「举报队列」进入 **`/moderation/reports`**（顶栏用户菜单仅进入 `/moderation`，与说明页指引一致）。
8. 普通会员直接访问 `/moderation/reports`：应回到治理说明页（由路由守卫处理）。
9. 打开举报队列时 **默认** 为「待处理」列表（`status=pending`）；可切换 **全部状态** 查看历史；版主仅见所辖版区。
10. 对任一行切换三态（例如 待处理→已驳回→已处置→待处理）：成功后列表刷新；核对 **`forum_reports`** 状态与处理人字段与根 **`forum-report-api`** 一致。

## C. 深链核验

11. 在队列点「查看详情」：帖子举报进入 `/posts/{postId}`；回复举报带上 `reply` query 锚到对应回复。

## 自动化

后端：`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj --filter "FullyQualifiedName~ForumReportTests"`；保留清理：`--filter "FullyQualifiedName~ForumReportRetentionPurgerTests"`。
