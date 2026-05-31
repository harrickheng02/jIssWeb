## Context

`JIssWeb.Model.Api` 是论坛领域唯一服务，`forum_posts` 与 `forum_replies` 集合当前没有 `State` 字段，版主删帖为硬删除。`ForumPostsController` 提供发帖/读帖/互动接口，`ForumMeController` 提供个人内容列表。前端 `useForumComposeForm.ts` 封装发帖逻辑，个人中心 `/me` 已有 posts / replies / favorites / settings 四个子路由。

本 change 要在不引入新集合的前提下，为帖子与回复补全"自编辑"和"草稿"能力，同时把版主硬删改为软删。

## Goals / Non-Goals

**Goals:**
- 帖子作者自编辑（标题、正文、Tags），回复作者自编辑（正文）
- 草稿 CRUD + 独立发布端点（与已发布帖子同表，State 字段隔离）
- 版主删帖/删回复改为软删，后台定时清理
- `UpdatedAtUtc` 追踪，前端"已编辑"标记
- 个人中心新增「草稿」子页

**Non-Goals:**
- 版本历史与编辑记录、富文本编辑器、协作编辑、草稿分享/多设备实时同步

## Decisions

### D1：草稿与已发布帖子同表（State 字段）
**选择**：在 `forum_posts` 集合新增 `State: "published" | "draft" | "deleted"` 字段，不新建 `forum_drafts` 集合。

**理由**：行业主流方案，减少集合数量，发布草稿只需 `UpdateOne(State = "published")`，无需跨集合原子操作。

**风险缓解**：所有公开查询（list、detail、replies）强制过滤 `State != "draft" && State != "deleted"`；新增公共辅助方法 `ForumPostFilters.Published()` 统一封装，防止遗漏。

**存量兼容**：现有文档无 `State` 字段，C# 模型默认值 `= "published"` 保证反序列化安全；但 Mongo 查询不能依赖默认值，需在服务启动时执行一次性迁移（`UpdateMany` 把无 `State` 文档设为 `"published"`），之后查询用简单 `Eq`。

---

### D2：Tags UseCount 差量更新
编辑帖子时计算 `旧 tags ∖ 新 tags`（减）和 `新 tags ∖ 旧 tags`（加），对两个差集分别执行 `UpdateMany Inc`。并发时两次写非原子，但 UseCount 为统计计数器（非严格余额），可接受极小概率的计数漂移。

---

### D3：草稿发布端点独立
选择 `POST /api/forum/posts/drafts/{draftId}/publish` 而非在 `POST /api/forum/posts` 加 `draftId` 参数。

**理由**：语义清晰，不污染创建帖子接口；符合 RESTful 子资源动作设计；路由字面量 `/drafts/` 在 ASP.NET Core 中优先于 `{postId}` 参数匹配，无冲突。

---

### D4：软删除与定时清理
版主删帖/删回复改为 `State = "deleted"` + `DeletedAtUtc = now`。新增 `DraftCleanupBackgroundService`（.NET `BackgroundService`）：
- 每 24 小时扫描一次
- 删除条件：`State = "deleted" AND DeletedAtUtc < now - RetentionDays`
- 级联删除：关联的 replies（仅帖子软删触发）、likes、favorites、notifications
- `RetentionDays` 可通过 `Forum:SoftDelete:RetentionDays`（默认 30）配置

版主审计端点查询时不过滤 `State`（或显式包含 `deleted`），DTO 中返回 `State` 字段供前端显示"已删除"标识。

---

### D5：回复软删除
`ForumReplyRecord` 同样新增 `State / DeletedAtUtc / DeletedBySub`，版主删回复走相同软删路径；`GET /api/forum/posts/{id}/replies` 过滤 `State = "published"`；已删回复 `PUT` 编辑返回 404。

---

### D6：前端 compose 复用单一 composable
`useForumComposeForm.ts` 扩展为接收可选 `mode: 'create' | 'edit' | 'draft-edit'` 和 `postId/draftId`，内部路由到对应 API；不新建独立 composable，减少重复代码。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| 公开查询遗漏 State 过滤导致草稿/已删帖泄露 | 封装 `ForumPostFilters.Published()` 辅助方法；集成测试验证 draft/deleted 不出现在公开列表 |
| 存量数据迁移失败导致帖子"消失"（被 State 过滤掉）| 迁移在 `Program.cs` 启动时同步执行，失败则终止启动；迁移幂等（UpdateMany with filter State not exists）|
| Tags UseCount 差量更新极小漂移 | UseCount 为展示计数，允许统计误差；后续可加定时对账任务 |
| 定时清理误删仍在举报流程中的帖子 | 清理前检查是否存在 `state = "open"` 的关联举报，有则跳过该帖子并记录日志 |
| 路由 `/posts/drafts` 与 `/posts/{postId}` 冲突 | ASP.NET Core 字面量段优先于参数段，已验证无冲突；单测覆盖 |

## Migration Plan

1. **启动迁移**（`ForumMongoSetup.EnsureStateFieldAsync`）：`UpdateMany({ State: { $exists: false } }, { $set: { State: "published" } })` 对 posts 和 replies 集合各执行一次，幂等安全。
2. **新增 Mongo 索引**：`{ State: 1, CreatedAtUtc: -1 }`（列表查询常用复合索引）；`{ AuthorSubId: 1, State: 1 }`（草稿列表）。
3. **回滚策略**：State 字段为新增非必填，回滚旧版本代码后旧查询（无 State 过滤）仍能读到所有帖子，不会丢数据。草稿帖子在旧版本会出现在公开列表（可接受短暂窗口），因此回滚应尽快跟进数据清理。

## Open Questions

- 草稿自动保存（防抖 vs 手动）：本 Issue 实现手动"保存草稿"按钮；自动保存可在后续 Issue 中扩展 `useForumComposeForm` 的防抖逻辑。
- 草稿数量上限：当前不设上限，后续如有滥用可加 `Forum:Draft:MaxPerUser` 配置项。
