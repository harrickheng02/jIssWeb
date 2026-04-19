## Why

论坛 Feed 与详情需要可交互的点赞与收藏能力；当前仅有计数占位或缺失关系数据，无法表达「当前用户是否已点赞/已收藏」与「我的收藏」列表。本变更建立最小可用的数据模型与 HTTP 契约，为后续通知、推荐等扩展打底。

## What Changes

- 新增点赞关系与收藏关系持久化（按用户 `sub` + 帖子 id 唯一），帖子冗余 `likeCount`（或等价字段）与列表/详情返回一致。
- 新增认证接口：点赞/取消、收藏/取消、分页「我的收藏」；操作幂等，未登录返回 401，帖子不存在返回 404。
- 扩展帖子列表与详情响应：在已有 likes 计数基础上增加 `likedByMe`、`favoritedByMe`（对已登录请求解析 JWT；匿名可为 false 或省略，以实现契约为准）。
- 帖子删除或不可见时，关系不可再写入；级联或清理策略在设计中明确。

## Capabilities

### New Capabilities

- `forum-post-like-favorite`: 帖子点赞与收藏的基础领域与 API 要求（关系表/集合、幂等、计数一致性、我的收藏分页、与 `forum-content-api` 字段对齐）。

### Modified Capabilities

- `forum-content-api`: 帖子列表与详情（及任何返回帖子摘要的端点）在需求层面增加「当前用户是否已点赞/已收藏」及点赞计数字段契约；明确匿名与已登录行为。
- `model-service`: 在 MongoDB 中持久化点赞/收藏关系与帖子计数更新；在 `/api/forum` 下实现上述新端点并满足 `forum-post-like-favorite` 与更新后的 `forum-content-api` 要求。

## Impact

- **后端**：`JIssWeb.Model.Api`（论坛路由、Mongo 集合与索引、事务或等价原子更新）、可能的领域模型与仓储。
- **契约**：OpenAPI/Swagger 与 BFF/前端类型；首页与详情组件需展示状态与调用新接口。
- **依赖**：JWT `sub` 与 `token-identity-consistency`、统一 `ApiResult` 与错误码（`shared-foundation`）。
