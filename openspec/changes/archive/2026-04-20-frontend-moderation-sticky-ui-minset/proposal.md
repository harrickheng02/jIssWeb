## Why

目前后端已交付版主/管理员的帖子置顶治理端点与审计查询，但前端缺少角色可感知的入口与操作面板，导致“版主与普通用户体验无差异”，治理能力难以演示与验收。

## What Changes

- 前端识别 access token 的 `forumRole`（member/moderator/admin），并在 UI 中呈现治理入口与权限态。
- 帖子列表与详情展示 `isSticky`（置顶标记），与后端置顶排序一致。
- 在帖子详情提供“置顶/取消置顶”操作（仅版主/管理员可见），对接 `POST /api/mod/posts/{postId}/sticky`。
- 在帖子详情提供“操作记录”面板，对接 `GET /api/mod/audit?targetType=post&targetId=...` 展示审计项。
- 完善错误与鉴权反馈（401 走登录/refresh 既有路径；403/404/500 显示可理解的提示）。

## Capabilities

### New Capabilities

- `forum-moderation-sticky-ui`: 论坛帖子置顶治理前端最小闭环（角色识别、详情页操作、审计列表、错误反馈）。

### Modified Capabilities

- `forum-homepage-shell`: 帖子卡片/列表对置顶状态可视化（置顶标记）与置顶优先排序的展示一致性。
- `frontend-app-shell`: 在鉴权态下暴露治理入口（仅当 `forumRole` 为 moderator/admin），并提供统一的 401/403 反馈与路由守卫联动约定。

## Impact

- **frontend**：新增/修改帖子详情与列表 UI；新增 moderation API client；新增审计面板组件；调整导航入口。
- **backend contracts**：依赖 `forum-content-api` 的 `isSticky` 字段与置顶优先排序；依赖 `POST /api/mod/posts/{postId}/sticky` 与 `GET /api/mod/audit`。
- **配置**：本地/环境需保证 user-service 签发的 token 含 `forumRole=moderator|admin`，且 Model.Api 具备版主版区映射配置以避免 403。
