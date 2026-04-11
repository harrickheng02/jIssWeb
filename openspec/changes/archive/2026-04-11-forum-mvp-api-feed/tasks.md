## 1. Model.Api 领域与持久化

- [x] 1.1 定义 Mongo 文档模型（帖子、回复）与 `sub` 作者字段；启动时创建必要索引（如 postId、createdAt）
- [x] 1.2 实现 `GET/POST /api/forum/posts` 与 `GET /api/forum/posts/{id}`，统一分页与错误响应形状
- [x] 1.3 实现回复列表与 `POST` 创建回复；创建回复时更新帖子 `commentCount`（或等价字段）
- [x] 1.4 写操作 `[Authorize]`，从 `User` claims 取 `sub` 作为作者；公开 GET 无需登录

## 2. 网关与本地联调

- [x] 2.1 在 `JIssWeb.Gateway.Api` 的 `ReverseProxy` 增加 `forum` route 与指向 Model.Api 的 cluster；Docker/compose 中上游与文档一致
- [x] 2.2 验证经 `http://localhost:5094/api/forum/...` 与 Bearer 透传行为

## 3. 前端对接

- [x] 3.1 新增 forum API 客户端（基址 `/api`，路径 `/api/forum/...`），请求携带现有 Bearer 拦截器
- [x] 3.2 `HomeView`：挂载时拉取帖子列表，映射到现有卡片结构；处理加载/空/错误
- [x] 3.3 增加帖子详情路由与页面，拉取详情与回复列表；未登录发帖/回复跳转 `/auth`
- [x] 3.4 发帖与回复表单提交对接 `POST`，成功后刷新列表或跳转详情

## 4. 规格与收尾

- [x] 4.1 实现完成后将 `openspec/specs/forum-content-api/spec.md`（及 yarp/model 增量）按变更归档流程合并到主 specs（随 `/opsx:apply` 或 archive 流程处理）
- [x] 4.2 与 `pm-plan` M1 Issue「论坛领域首组 API」验收项自检：列表/详情/发帖/回复可演示
