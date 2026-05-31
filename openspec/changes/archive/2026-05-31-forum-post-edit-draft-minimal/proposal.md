## Why

论坛用户目前发布帖子或回复后无法修改，也无法保存草稿再择机发布，导致发错内容只能依赖版主删除、起草长帖无法中途暂存。这是 M3 阶段用户留存与内容质量提升的基础能力。

## What Changes

- **帖子自编辑**：帖子作者可通过 `PUT /api/forum/posts/{postId}` 修改标题、正文与标签；非作者返回 403；Tags UseCount 做差量更新。
- **回复自编辑**：回复作者可通过 `PUT /api/forum/posts/{postId}/replies/{replyId}` 修改正文；非作者返回 403；锁帖不影响存量回复编辑。
- **草稿生命周期**：新增草稿 CRUD（创建/更新/删除/列表）及独立发布端点 `POST /api/forum/posts/drafts/{id}/publish`；草稿与已发布帖子同存于 `forum_posts` 集合，通过 `State` 字段区分。
- **软删除改造**：**BREAKING** ── 版主删帖/删回复由硬删改为软删（`State = "deleted"`），保留 30 天后后台定期硬删；所有公开查询加 `State` 过滤器。
- **编辑时间追踪**：`ForumPostRecord` 与 `ForumReplyRecord` 新增 `UpdatedAtUtc` 字段，DTO 同步返回，前端显示"已编辑"标记。
- **个人中心草稿页**：新增 `/me/drafts` 路由与 `MeDraftsView.vue`，个人中心导航追加「草稿」入口并显示草稿数。

## Capabilities

### New Capabilities

- `forum-post-self-edit`: 帖子与回复作者自编辑接口、权限校验、Tags UseCount 差量更新、UpdatedAtUtc 追踪
- `forum-draft-lifecycle`: 草稿创建/更新/删除/列表/发布完整生命周期，包含 State 字段模型与公开查询隔离
- `forum-soft-delete`: 帖子与回复软删除替换硬删除，后台定时清理策略，版主视角可见已删内容标识

### Modified Capabilities

- `forum-content-api`: 新增 PUT 编辑端点；`GET /api/forum/posts` 列表及详情增加 State 过滤约定；`ForumPostRecord` 新增 `State`、`UpdatedAtUtc`、`DeletedAtUtc` 字段；`ReplyDto` 新增 `UpdatedAtUtc`

## Impact

- **服务边界**：JIssWeb.Model.Api（主要变更）；无跨服务 API 变动
- **数据库**：`forum_posts` 集合新增 `State`、`UpdatedAtUtc`、`DeletedAtUtc`、`DeletedBySub` 字段；`forum_replies` 同步新增；存量数据需一次性迁移（State = "published"）
- **后台服务**：新增 `DraftCleanupBackgroundService`（.NET `BackgroundService`），配置保留期默认 30 天
- **前端**：`useForumComposeForm.ts` 扩展为支持编辑模式与草稿模式；新增 `MeDraftsView.vue`；帖子详情与回复列表新增"编辑"入口及"已编辑"标记；所有样式遵循 `forum-tokens.css` CSS 变量约束

## Non-goals

- 版本历史与编辑记录（可在本 Issue 后单开）
- 富文本编辑器升级（独立 Issue）
- 协作编辑（独立产品）
- 草稿分享 / 多设备实时同步（独立 Issue）
- 草稿内容恢复（回收站 UI）
- 版主软删帖子的前台恢复入口（可在 Issue #18 后续迭代）
