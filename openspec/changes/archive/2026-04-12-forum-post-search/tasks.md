## 1. 后端搜索与限流

- [x] 1.1 扩展 `GET /api/forum/posts` 支持可选参数 `q`：Mongo `$or` 匹配标题（转义后大小写不敏感子串）与 `AuthorSubId`（子串），并与现有 `boardId` 过滤组合；trim 后空的 `q` 返回 400
- [x] 1.2 按设计补充索引或调整查询；若项目惯例需要则做迁移或启动时确保索引
- [x] 1.3 对带非空 `q` 的请求做限流（IP / X-Forwarded-For），可配置配额，超限 429 且响应体符合统一错误格式
- [x] 1.4 用集成或 API 测试覆盖搜索与限流路径，风格与现有论坛测试一致（`backend/tests/JIssWeb.Model.Api.Tests/ForumPostsSearchTests.cs`、`ForumSearchRateLimitTests.cs`；`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj --filter "FullyQualifiedName~ForumPostsSearchTests|FullyQualifiedName~ForumSearchRateLimitTests"`）

## 2. 前端首页搜索

- [x] 2.1 顶栏搜索对接论坛帖子接口，传 `q`、`page`、`pageSize`，复用 Feed 卡片摘要字段映射
- [x] 2.2 实现防抖（约 300ms）、回车立即提交、trim 后为空则不请求
- [x] 2.3 在搜索驱动的列表区展示加载、空结果、错误与 429 状态

## 3. 规范与运维

- [x] 3.1 增量已合入 `openspec/specs/`；`openspec archive forum-post-search -y --skip-specs` → `openspec/changes/archive/2026-04-12-forum-post-search`
