## Context

- **pm-plan**：Issue #2「个人中心与内容管理最小集」归属 M2，验收强调 `sub`、我的帖子/回复/收藏、Profile 衔接、空失败态与设置占位。
- **现状**：`ForumMeController` 已实现 `GET /api/forum/me/posts` 与 `GET /api/forum/me/replies`（按 `sub` 过滤）；`openspec/specs/forum-content-api` 已收录对应要求。`ForumEngagementService.ListFavoritePostsAsync` 已实现收藏列表领域逻辑；`openspec/specs/forum-post-like-favorite` 规定 `GET /api/forum/me/favorites`。前端存在 `/profile` 与用户菜单，但缺少统一的 `/me` 类壳与对上述 me 端点的 API 封装与列表页。
- **约束**：身份与过滤以 `token-identity-consistency` 为准；个人资料仍以 customer profile 的 `api/profile` 为权威；收藏列表行为以 `forum-post-like-favorite` 为准。

## Goals / Non-Goals

**Goals:**

- 提供可发现、可路由的个人中心最小壳，并能演示我的帖子、我的回复、我的收藏三类分页列表（含加载/空/错误态）。
- 资料编辑继续复用现有 Profile 页面或等价 API，避免重复客档语义。
- 与 pm-plan **非目标**一致：不做完整安全中心、第三方绑定、全渠道消息设置、多级收藏夹、独立「赞过的帖子」列表。

**Non-Goals:**

- 不改写点赞/收藏领域模型或 `forum-post-like-favorite` 中已定义的变异与计数语义（除非发现与 `me/favorites` 接线冲突的缺陷修复）。
- 不引入 BFF 聚合层作为本变更前提（直连 model `api/forum` 即可）。

## Decisions

1. **路由形状**：采用受保护父路由（例如 `/me`）+ 子路由或 Tab（`posts`、`replies`、`favorites`、`settings`）；「资料」导航至既有 `/profile`（或内嵌 iframe 式不推荐——优先 `router-link`），以满足「与现有 Profile 衔接」且单一编辑入口。
2. **API 层**：在 `clients.ts`（或既有 model 客户端模块）增加 `listMyForumPosts`、`listMyForumReplies`、`listMyForumFavorites`，与公开 `listForumPosts` 共享 `ForumPostListItem` 类型；分页参数与后端约定一致（`page`/`pageSize` 与现有论坛列表相同）。
3. **列表 UI**：复用首页 Feed 帖子卡片组件渲染「我的帖子」「我的收藏」；「我的回复」使用独立紧凑列表（正文预览 + 帖子链接），字段以后端 DTO 为准。
4. **`GET /api/forum/me/favorites`**：若仓库中尚无控制器 action，则在 `ForumMeController`（保持 `api/forum/me` 前缀）新增 GET，注入 `ForumEngagementService` 并映射 `ForumDtoMapping.ToListItem`；若已存在则本变更仅前端与验收。集成测试与 `ForumPostLikeFavorite` 规范场景对齐。
5. **入口**：在 `HeaderUserMenu` 增加「个人中心」链至 `/me`；保留「个人资料」或合并为子项由实现择一，但须满足从个人中心可达各子能力。

## Risks / Trade-offs

- **[Risk] 收藏列表 `totalCount` 与孤儿清理**：设计已述分页总数可能含已删帖关系直至清理完成 → **缓解**：UI 展示「共 N 条」与当前页条数差异时以 spec 为准，不在本变更引入新计数语义。
- **[Risk] `useForumPostEngagement` 与 `clients` 不同步**：若本地有未合并的点赞 API → **缓解**：合并前统一 `npm run build` / 类型检查，保证 `clients` 导出完整。
- **[Trade-off] 设置 Tab**：仅主题切换与退出占位，深度账户设置明确推迟，避免与 pm 非目标重叠。

## Migration Plan

- 纯新增路由与 API 调用，无数据迁移。部署顺序：先后端 `me/favorites`（若缺）→ 再前端，避免前端 404。
- 回滚：移除路由与菜单项即可；后端新端点可保留兼容（一般不删）。

## Open Questions

- 回复列表 DTO 是否已包含 `postTitle` / `postId` 以利深链；若字段不足，是否在 `forum-content-api` 增补展示字段（可作为 follow-up change，本变更优先以后端已有字段排版）。
