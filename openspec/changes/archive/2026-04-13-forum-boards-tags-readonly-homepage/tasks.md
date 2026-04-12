## 1. API

- [x] 1.1 在 Model.Api 为 `GET /api/forum/posts` 增加可选 `tag` 筛选（与 `boardId`、`q` AND），空白 `tag` 400；实现与 `forum-content-api` delta 一致。
- [x] 1.2 新增匿名 `GET /api/forum/tags/popular`（支持可选 `boardId`、可选 `limit` 及上限），Mongo 从帖子 `Tags` 聚合；未知 `boardId` 400。
- [x] 1.3 为 1.1、1.2 补充集成测试或扩展现有 Forum 测试夹具。验证：`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj`；用例：`ForumPostsSearchTests`（`tag` / `tag`+`q` / `tag`+`q`+`boardId`）、`ForumMeEndpointsTests`（发帖 `tags`）。

## 2. 前端

- [x] 2.1 `clients.ts`：`listForumPosts` 增加可选 `tag`；新增 `getForumPopularTags(boardId?, limit?)`。验证：`npm run build`（`frontend/`）。
- [x] 2.2 首页：`HomeView.vue` + `useForumPopularTags` / `useForumHomeFeed` / `useForumBoards` / `useForumComposeForm` + `utils/forumPostDisplay.ts`；热门标签、列表与路由 `tag`、板块加载、发帖表单与展示函数拆分。
- [x] 2.3 列表空/错/加载与现有 `fetchPosts` 行为一致；`tag` 激活时空态文案可与纯无帖区分（若 spec 需要）。

## 3. 网关与收尾

- [x] 3.1 确认 Yarp/网关已转发新 path；否则追加路由。
- [x] 3.2 `openspec archive` 前将本 change 的 spec delta 合入 `openspec/specs/`（按项目 archive 流程执行）。
