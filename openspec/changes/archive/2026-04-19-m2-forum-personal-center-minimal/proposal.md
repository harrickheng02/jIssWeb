## Why

M2 规划（`scripts/github-sync/pm-plan.yaml` Issue #2）要求可演示的「个人中心与内容管理最小集」：在 JWT `sub` 一致前提下聚合资料、我的帖子/回复、我的收藏与基础设置占位。后端已有论坛 `me` 读模型与 `forum-post-like-favorite` 契约，但前端缺少统一入口、路由与列表体验，无法独立验收 pm 条目。

## What Changes

- 新增受保护「个人中心」路由（或等价路径），提供从该壳到达「我的帖子」「我的回复」「我的收藏」与现有「个人资料」的路径；各列表分页、空态、失败态可演示。
- 前端封装并调用 `GET /api/forum/me/posts`、`GET /api/forum/me/replies`、`GET /api/forum/me/favorites`（路径以 `forum-post-like-favorite` 与实现对齐），列表项字段与公开帖子摘要合同一致或可核验映射。
- 顶栏或用户菜单增加「个人中心」入口，与现有登录/退出、主题（若有）不冲突。
- 若 `GET /api/forum/me/favorites` 尚未在 Model API 暴露，则在本变更中补齐控制器与集成测试，使行为符合既有 `forum-post-like-favorite` 规范。

## Capabilities

### New Capabilities

- `forum-personal-center`：个人中心信息架构、子导航、与 Profile/论坛 me 接口的集成要求，以及空态/失败/设置占位等体验验收。

### Modified Capabilities

- `frontend-app-shell`：增加个人中心相关受保护路由与全局可发现入口（例如用户菜单项），与现有 `requiresAuth` 与 Profile 路由协同。

## Impact

- **前端**：`frontend/src/router`、`HeaderUserMenu`（或等价布局）、新建视图/组件、`api/clients` 中 model 客户端方法。
- **后端**（仅当缺省时）：`JIssWeb.Model.Api` 中 `ForumMeController` 或等价路由、`ForumEngagementService` 接线、`ForumMeEndpointsTests` 或同类测试。
- **依赖规范**（引用不重复定义）：`openspec/specs/token-identity-consistency`、`openspec/specs/forum-content-api`（我的帖子/回复）、`openspec/specs/forum-post-like-favorite`、`openspec/specs/customer-profile-service` / `user-profile-record`。
