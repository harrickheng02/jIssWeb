## 1. 数据模型与迁移

- [x] 1.1 `ForumPostRecord` 新增字段：`State`（默认 `"published"`）、`UpdatedAtUtc`、`DeletedAtUtc`、`DeletedBySub`
- [x] 1.2 `ForumReplyRecord` 新增字段：`State`（默认 `"published"`）、`UpdatedAtUtc`、`DeletedAtUtc`、`DeletedBySub`
- [x] 1.3 在 `ForumMongoSetup` 中新增 `EnsureStateFieldAsync`：对无 `State` 字段的 posts/replies 执行 `UpdateMany($set: { State: "published" })`（幂等）
- [x] 1.4 在 `ForumMongoSetup` 中新增复合索引：`{ State, CreatedAtUtc desc }`（posts）、`{ AuthorSubId, State }`（posts）
- [x] 1.5 在 `Program.cs` 启动时调用 `EnsureStateFieldAsync`（迁移必须在服务接受请求前完成）

## 2. 公开查询隔离（State 过滤器）

- [x] 2.1 新增辅助方法 `ForumPostFilters.Published()`，返回 `Filter.Eq(State, "published")`，供所有公开查询复用
- [x] 2.2 `ForumPostsController.List` 加入 `Published()` 过滤器
- [x] 2.3 `ForumPostsController.GetById` 加入 State 判断：`deleted` → 404，`draft` 且非作者 → 404
- [x] 2.4 `ForumPostsController.GetReplies` 对回复列表加入 `Published()` 过滤器（Reply State）
- [x] 2.5 `ForumMeController.MyPosts` 仅返回 `State: "published"` 的帖子（草稿由单独 `/me/drafts` 返回）
- [x] 2.6 集成测试：draft/deleted 帖子不出现在公开列表与详情（2 个测试场景）

## 3. 软删除改造

- [x] 3.1 修改 `ForumModerationDeleteService.DeletePostAsync`：改为 `UpdateOne(State="deleted", DeletedAtUtc, DeletedBySub)` + Tags UseCount -1 delta
- [x] 3.2 修改 `ForumModerationDeleteService.DeleteReplyAsync`：改为软删 + parent post `CommentCount -1`
- [x] 3.3 版主/管理员审计端点（`ModPostsController` 或等价）查询时包含 `deleted` State，DTO 中返回 `state` 字段
- [x] 3.4 集成测试：软删后公开接口返回 404；版主接口可见 state=deleted 完整内容

## 4. 后台定时清理服务

- [x] 4.1 新增 `DraftCleanupBackgroundService`（`BackgroundService`），每 24 小时扫描一次
- [x] 4.2 清理逻辑：`State="deleted" AND DeletedAtUtc < now - RetentionDays`；跳过存在 open 举报的帖子并记录警告日志
- [x] 4.3 级联删除关联 replies、likes、favorites、notifications（帖子清理时）
- [x] 4.4 在 `appsettings.Local.example.json` 新增 `Forum:SoftDelete:RetentionDays` 配置键（默认 30，附注释）
- [x] 4.5 在 `Program.cs` 注册 `DraftCleanupBackgroundService`

## 5. 帖子自编辑接口

- [x] 5.1 编写集成测试：作者编辑成功（200 + UpdatedAtUtc 非空）、非作者编辑 403、deleted 帖子编辑 404（先写测试）
- [x] 5.2 `ForumPostsController` 新增 `[HttpPut("{postId}")]` 端点：校验 State、作者身份；更新 Title/Body/Tags/Excerpt/UpdatedAtUtc
- [x] 5.3 实现 Tags UseCount 差量更新逻辑（`旧∖新` -1，`新∖旧` +1，交集跳过），封装为私有方法
- [x] 5.4 运行测试 5.1 直至全绿

## 6. 回复自编辑接口

- [x] 6.1 编写集成测试：作者编辑回复成功、非作者 403、locked 帖子的回复仍可编辑（先写测试）
- [x] 6.2 `ForumPostsController` 新增 `[HttpPut("{postId}/replies/{replyId}")]` 端点：校验回复 State 与作者；更新 Body/UpdatedAtUtc
- [x] 6.3 运行测试 6.1 直至全绿

## 7. DTO 更新（UpdatedAtUtc）

- [x] 7.1 `PostListItemDto`、`PostDetailDto` 新增 `UpdatedAtUtc` 字段（`DateTime?`）
- [x] 7.2 `ReplyDto` 新增 `UpdatedAtUtc` 字段（`DateTime?`）
- [x] 7.3 `ForumDtoMapping.ToListItem`、`MapDetail`、`ToReplyDto` 映射 `UpdatedAtUtc`

## 8. 草稿 CRUD 接口

- [x] 8.1 编写集成测试：创建草稿、更新草稿、删除草稿、草稿不出现在公开列表（先写测试）
- [x] 8.2 新增 `ForumDraftsController`（路由 `api/forum/posts/drafts`，`[Authorize]`）
- [x] 8.3 实现 `POST /drafts`：创建 `State="draft"` 的 ForumPostRecord，返回 `{ id, state }`
- [x] 8.4 实现 `PUT /drafts/{draftId}`：校验作者与 `State="draft"`，更新字段
- [x] 8.5 实现 `DELETE /drafts/{draftId}`：校验作者，物理删除
- [x] 8.6 运行测试 8.1 直至全绿

## 9. 草稿发布接口

- [x] 9.1 编写集成测试：合法发布、标题/正文缺失 400、boardId 无效 400、非作者发布 403（先写测试）
- [x] 9.2 实现 `POST /drafts/{draftId}/publish`：校验 title/body/boardId；`UpdateOne(State="published")`；Tags UseCount +1（新发布标签）
- [x] 9.3 运行测试 9.1 直至全绿

## 10. 草稿列表接口

- [x] 10.1 编写集成测试：我的草稿列表分页、空列表（先写测试）
- [x] 10.2 `ForumMeController` 新增 `[HttpGet("drafts")]`：过滤 `State="draft" AND AuthorSubId=sub`，按 `CreatedAtUtc desc` 分页返回
- [x] 10.3 运行测试 10.1 直至全绿

## 11. 前端 API 客户端扩展

- [x] 11.1 在 `frontend/src/api/clients.ts` 新增以下函数（复用 `createClient("/api/forum")`）：
  - `updateForumPost(postId, body)` → `PUT /api/forum/posts/{postId}`
  - `updateForumReply(postId, replyId, body)` → `PUT /api/forum/posts/{postId}/replies/{replyId}`
  - `createDraft(body)` → `POST /api/forum/posts/drafts`
  - `updateDraft(draftId, body)` → `PUT /api/forum/posts/drafts/{draftId}`
  - `deleteDraft(draftId)` → `DELETE /api/forum/posts/drafts/{draftId}`
  - `publishDraft(draftId)` → `POST /api/forum/posts/drafts/{draftId}/publish`
  - `getMyDrafts(page, pageSize)` → `GET /api/forum/me/drafts`

## 12. 前端发帖表单扩展（编辑 & 草稿模式）

- [x] 12.1 扩展 `useForumComposeForm.ts`，新增 `mode: 'create' | 'edit' | 'draft-edit'`、`editTargetId: string | null`
- [x] 12.2 `edit` 模式下 `submitCompose()` 调用 `updateForumPost`；`draft-edit` 模式调用 `updateDraft`
- [x] 12.3 新增 `saveDraft()` 方法：mode=create 时调用 `createDraft`，mode=draft-edit 时调用 `updateDraft`
- [x] 12.4 Vitest 单元测试：`submitCompose` 在 edit 模式调用正确 API（mock axios）

## 13. 前端帖子详情页编辑入口

- [x] 13.1 `PostDetailView.vue` 帖子标题区加入「编辑」按钮（仅当 `authStore.userId === post.authorId`）
- [x] 13.2 点击「编辑」打开 compose dialog，初始化 `mode='edit'`，预填 title/body/tags/boardId
- [x] 13.3 帖子 meta 信息区：当 `post.updatedAtUtc` 非空时显示「已编辑 X 时间前」
- [x] 13.4 回复列表每条回复：当作者为当前用户时，显示「编辑」按钮，点击后打开行内编辑 textarea
- [x] 13.5 回复旁：当 `reply.updatedAtUtc` 非空时显示「已编辑」标记

## 14. 前端草稿页

- [x] 14.1 新增 `frontend/src/views/me/MeDraftsView.vue`：展示草稿分页列表，每项有「继续编辑」和「删除」操作
- [x] 14.2 在 `frontend/src/router/index.ts` 的 `/me` children 中新增 `{ path: 'drafts', name: 'me-drafts', ... }`
- [x] 14.3 个人中心侧边导航（`PersonalCenterLayout.vue`）新增「草稿」入口，显示草稿计数徽标
- [x] 14.4 发帖 compose dialog 新增「保存草稿」按钮（`el-button` 非 primary），调用 `saveDraft()`

## 16. 用户自删帖子/回复（软删除）+ 0 互动永久硬删除

- [x] 16.1 更新 `forum-soft-delete/spec.md`：补充作者自删帖子、回复、GetById 可见性、0 互动永久删的 SHALL 需求
- [x] 16.2 `ForumPostsController.GetById`：State=deleted 且 DeletedBySub==AuthorSubId==sub → 200（不增 ViewCount）；否则 404
- [x] 16.3 新增 `DELETE /api/forum/posts/{postId}`（作者软删帖子）：State→deleted，Tags -1
- [x] 16.4 新增 `DELETE /api/forum/posts/{postId}/replies/{replyId}`（作者软删回复）：State→deleted，CommentCount -1
- [x] 16.5 新增 `DELETE /api/forum/posts/{postId}/permanent`（0 互动永久删）：验证 likes/comments/favorites 全为 0，级联物理删
- [x] 16.6 集成测试 `ForumSelfDeleteTests.cs`（10 个场景全绿）
- [x] 16.7 前端 `clients.ts`：新增 `deleteForumPost`、`deleteForumReply`、`permanentDeleteForumPost`
- [x] 16.8 前端 `PostDetailView.vue`：作者删除帖子按钮 + 已删横幅 + 永久删除按钮（0 互动时）+ 作者删除回复按钮

## 15. 全量验证

- [x] 15.1 运行 `cd backend && dotnet test tests/JIssWeb.Model.Api.Tests` 全绿（3 个跨 fixture 污染用例为预存在缺陷，隔离运行全部通过）
- [x] 15.2 运行 `cd frontend && npm test` 全绿（7 个测试，含新增 4 个 compose form 单测）
- [ ] 15.3 本地联调：创建草稿 → 编辑草稿 → 发布草稿 → 编辑已发布帖子 → 编辑回复 → 版主软删帖子 → 公开列表不可见
- [x] 15.4 `change-review`：对照 specs 中所有 SHALL/MUST 确认无遗漏；修复以下 3 项：
  - `CreateDraft` 响应补充 `state: "draft"` 字段（spec 明确要求）
  - `GetById` 允许版主/管理员查看任意 `state=deleted` 帖子（task 3.3）
  - `PostListItemDto`/`PostDetailDto` 暴露 `deletedAtUtc`/`deletedBySub`；前端永久删除按钮改为检查 `post.deletedBySub === auth.sub`
