## Why

论坛首页已有「精华」tab 占位，但 `featured` feed 实际等同于 latest，加精功能从未上线（Issue #20）。版主缺少一个轻量的内容推荐工具，导致优质帖子无法与普通帖区分呈现。

## What Changes

- **新增后端字段**：`ForumPostRecord` 追加 `IsFeatured`、`FeaturedAtUtc`、`FeaturedBySub` 字段，并建立复合索引 `{ IsFeatured: 1, FeaturedAtUtc: -1 }`
- **新增版主端点**：`POST /api/mod/posts/{id}/featured`（body: `{ "isFeatured": bool }`），需版主鉴权，写审计记录（actionLabel: "加精"/"取消精华"）
- **扩展查询过滤**：`GET /api/forum/posts` 新增 `featured=true` 独立 filter，与 boardId/tag/q 正交；精华排序按 `FeaturedAtUtc` 降序，null 回退 `CreatedAtUtc` 降序
- **扩展 DTO**：公开帖子 DTO 追加 `isFeatured` 字段
- **前端 API 层**：`clients.ts` 扩展帖子 DTO、增加 `setForumPostFeatured()` 函数、`getForumPosts` 支持 `featured?` 参数
- **前端 composable**：`useForumHomeFeed` 中 `feedSort==='featured'` 时传 `featured=true`，激活真实数据流
- **前端组件**：`ForumPostGovernancePanel` 增加加精/取消精华按钮；帖子列表卡和详情页展示轻量精华角标

## Capabilities

### New Capabilities

- `forum-moderation-featured-ops`: 版主加精/取消精华端点、鉴权规则、审计记录契约
- `forum-post-featured-feed`: 精华帖查询过滤（`featured=true` 参数）、精华帖排序规则、DTO `isFeatured` 字段契约

### Modified Capabilities

- `forum-moderation-post-ops`: 在现有置顶/锁帖端点规范基础上追加加精端点的鉴权与审计行为规则（结构一致，新增 featured 场景）

## Impact

- **服务边界**：`JIssWeb.Model.Api`（后端唯一变更服务）
- **数据库**：`ForumPostRecord` 集合新增字段 + 复合索引
- **API 契约**：`GET /api/forum/posts` 新增 `featured` 查询参数；`POST /api/mod/posts/{id}/featured` 新端点
- **前端**：`frontend/src/api/clients.ts`、`frontend/src/composables/useForumHomeFeed.ts`、`frontend/src/components/ForumPostGovernancePanel.vue`，以及帖子列表卡与详情页组件；所有样式遵循 `forum-tokens.css` 约束
- **依赖**：`openspec/specs/forum-moderation-post-ops`（已存在）、Issue #7（已关闭）

## Non-goals

- 沉帖（降权屏蔽）操作
- 加精多级评级（金精/银精等）
- 批量加精/取消精华
- 精华帖专属详情页或独立频道页
