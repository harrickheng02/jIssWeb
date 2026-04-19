## 1. 数据与索引

- [x] 1.1 为帖子文档确认或新增 `LikeCount` 字段及非负约束；定义 likes/favorites 集合文档形状与 `(PostId, UserSubId)` 唯一索引
- [x] 1.2 实现点赞/取消、收藏/取消的写入路径：事务或等价原子更新，保证计数与关系一致
- [x] 1.3 帖子删除或不可见时清理或失效关系（与设计 Open Questions 选定策略一致）

## 2. API（Model.Api）

- [x] 2.1 实现 `POST`/`DELETE` `/api/forum/posts/{postId}/like` 与 `.../favorite`：401/404、幂等、成功体含 `likeCount`、`likedByMe`、`favoritedByMe`
- [x] 2.2 实现 `GET /api/forum/me/favorites`：分页、按收藏时间倒序、401、摘要字段与公开列表对齐
- [x] 2.3 扩展 `GET /api/forum/posts`、`GET /api/forum/posts/{postId}`、当前用户帖子列表：返回 `likeCount`、`likedByMe`、`favoritedByMe`（匿名行为按 delta spec）

## 3. 契约与前端

- [x] 3.1 更新 Swagger/OpenAPI 或共享 DTO，与 `forum-post-like-favorite` 及 delta 一致
- [x] 3.2 前端 Feed/详情：展示点赞与收藏状态，调用变更接口；未登录引导登录或禁用操作

## 4. 验证

- [x] 4.1 按 `openspec/changes/post-like-favorite-foundation/specs/**/spec.md` 场景验收；自动化：`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj`；手工补充：匿名/登录下列表与详情、`likedByMe`/`favoritedByMe`、点赞/收藏幂等与我的收藏分页
- [x] 4.2 `dotnet build` 与涉及前端的 `npm run build` 通过

## 5. 前端 UX 与读模型

- [x] 5.1 Feed/详情：乐观更新、失败快照回滚、请求中禁用、250ms 冷却防连点（`useForumPostEngagement`）
- [x] 5.2 Feed：`visibilitychange` 可见时重拉列表（SWR 轻量版）

## 6. Redis 点赞数缓存（可选，与 `design.md` §6）

- [x] 6.1 `RedisSettings.ConnectionString` 非空时：写穿 `SET`、`GetMany`/`Get` 读缓存、`Remove` 删帖删键；未配置 Redis 时回退仅 Mongo
