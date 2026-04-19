## Context

论坛内容由 model 服务承载（`/api/forum`），帖子存 MongoDB，身份以 JWT `sub` 为准（`token-identity-consistency`）。需在不影响现有列表/搜索/回复的前提下，增加点赞与收藏的关系数据、计数与只读扩展字段。

## Goals / Non-Goals

**Goals:**

- 点赞/收藏关系可查询、可幂等写入；帖子 `likeCount` 与关系表一致。
- 列表/详情对已登录用户返回 `likedByMe`、`favoritedByMe`；匿名请求不强制携带（可为 false 或省略，与 `forum-content-api` delta 一致）。
- 「我的收藏」分页列表，按收藏时间倒序。

**Non-Goals:**

- 点赞/收藏通知、反作弊、收藏夹分组、点赞用户列表。
- 跨服务事件总线；仅单服务内一致即可。

## Decisions

1. **存储**：MongoDB 集合，例如 `forum_post_likes`、`forum_post_favorites`，文档含 `PostId`、`UserSubId`、`CreatedAt`；复合唯一索引 `(PostId, UserSubId)`（或 `(UserSubId, PostId)` 视查询为主）。帖子文档字段 `LikeCount`（int，非负）。
2. **计数更新**：同一请求内使用事务（多文档）或等价原子模式：插入关系成功则 `LikeCount`+1；删除成功则 -1；依赖唯一索引防止重复插入；取消重复删除不扣减。
3. **API 形状**：`POST`/`DELETE` `/api/forum/posts/{postId}/like` 与 `.../favorite`；`GET /api/forum/me/favorites` 分页（query：`page`/`pageSize` 或与现有论坛列表一致）。响应使用统一 `ApiResult`。
4. **列表扩展**：对 `GET /api/forum/posts` 与 `GET /api/forum/posts/{postId}`，在 Bearer 有效时批量解析当前 `sub` 与当页 post id，批量查询关系并映射到 `likedByMe`/`favoritedByMe`；无 Token 时见 `specs/forum-content-api/spec.md`（可省略两字段；若出现则须为 `false`）。
5. **帖子删除**：帖子物理删除或标记删除时，异步或同步清理对应关系文档；清理前对新点赞返回 404。

**备选**：不以 Redis 作为**唯一持久**点赞计数（需 Mongo 关系与帖子字段）；允许见 §6 用 Redis 作**读缓存**写穿。

## Risks / Trade-offs

- **[Risk]** 高并发下计数漂移 → **Mitigation**：事务 + 唯一键；可选低频对账任务（非本阶段必做）。
- **[Risk]** 列表批量查关系增加延迟 → **Mitigation**：按页 post id `$in` 批量查、限制 `pageSize` 上限。
- **[Trade-off]** 收藏数不在帖子冗余 → 列表不展示收藏总数时以 count 查询或省略，减少写放大。

## Migration Plan

1. 部署空集合与索引创建脚本或启动时确保索引。
2. 已有帖子 `LikeCount` 若为占位，可一次性迁移为 0 或从关系表重算（数据量小可全表扫描）。
3. 回滚：停写新端点并前端降级；旧客户端忽略新字段无影响。

## 前端交互与读模型（优化策略）

1. **乐观更新**：点击后立即按切换意图更新 `likedByMe`/`favoritedByMe` 与本地计数（点赞 ±1、收藏 ±1），不等待网络；成功后用接口返回体覆盖同一帖子字段。
2. **请求中与冷却**：同一帖子同一操作（赞/收藏）在请求进行中须**防止重复提交**（可为禁用按钮，或在入口用 pending 标志短路并保持可点态以免 `cursor: wait` 盖住乐观 UI）；请求结束后 **250ms** 冷却，防止连点；与「乐观状态」分离——乐观只负责即时 UI，pending/cooldown 只负责防重复提交。
3. **失败回滚**：接口失败或抛错时，用点击前快照恢复 `likeCount`/`favoriteCount`/`likedByMe`/`favoritedByMe`，并 `ElMessage` 提示。
4. **读侧 SWR（Stale-While-Revalidate）**：Feed 在 `document.visibilityState === 'visible'`（从后台切回前台）时重新请求列表，用服务端数据纠正多标签或长时间后台导致的偏差。
5. **后端幂等与计数**：沿用「唯一索引 + 事务/原子更新」；重复写入不产生重复关系、重复取消不重复扣减（见上文 Decisions）。
6. **Redis 点赞数缓存（压测读路径）**：`RedisSettings.ConnectionString` 非空时注册 `IConnectionMultiplexer`；键 `{KeyPrefix}forum:lc:{postId}` 存当前 `likeCount`。**写穿**：`GetSnapshot`（及所有经其返回的快照）后 `SET`；删帖时 `DEL`。列表/详情在 Redis 命中时用缓存展示 `LikeCount`，未命中回退 Mongo 字段。**权威数据仍为 Mongo**（关系 + `LikeCount`）；Redis 仅减轻热点读、便于压测观测。若需「仅 Redis 计数、异步落库」或 **INCR 主写**，属后续变更。

## 已决（原 Open Questions）

- **匿名 `likedByMe`/`favoritedByMe`：** 以 `specs/forum-content-api/spec.md` 为准：可省略；若返回则须为 `false`。
- **我的收藏与已删帖：** 不返回已删帖；遍历收藏时若帖子不存在则删除孤儿收藏文档并跳过该项（`ForumEngagementService.ListFavoritePostsAsync`）。分页 `totalCount` 仍按收藏关系计数，与「当前页条数」可能短期不一致直至孤儿被清完。
