## 1. 论坛 Model API

- [x] 1.1 实现 `GET /api/forum/me/posts`：`[Authorize]`，分页与公开列表校验一致，按 `AuthorSubId == sub` 过滤，返回与列表项一致的 DTO 形态
- [x] 1.2 实现 `GET /api/forum/me/replies`：`[Authorize]`，分页参数与错误约定与论坛 API 一致，按回复 `AuthorSubId == sub` 过滤，返回含 `postId`、正文、时间等字段
- [x] 1.3 在同一 `ForumPostsController`（或约定文件）中注册 `me` 路由，确保与 `{id}` / `{postId}` 路由不冲突（`me` 优先匹配）
- [x] 1.4 在 `ForumMongoSetup.EnsureIndexes` 为 `forum_posts`、`forum_replies` 增加 `AuthorSubId` + `CreatedAtUtc` 的复合索引以支撑按用户分页

## 2. 核验

- [x] 2.1 联调或手工验证：有效 token 仅返回该 `sub` 的帖子/回复；无 token 为 401；不引入可覆盖 `sub` 的 query 用户参数
  - **记录**：集成测试 `backend/tests/JIssWeb.Model.Api.Tests`（xUnit + `WebApplicationFactory` + Mongo2Go + Moq 替换 `IConnectionMultiplexer`），`ForumMeEndpointsTests`：`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj`。覆盖：无 Bearer→401；非法分页→400/`INVALID_PAGINATION`；`user-a` token 仅返回该 `sub` 的帖子与回复。
