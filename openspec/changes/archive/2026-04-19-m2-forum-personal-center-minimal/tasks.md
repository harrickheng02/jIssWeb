## 1. 后端接线（仅当缺失时）

- [x] 1.1 核对 `GET /api/forum/me/favorites` 是否已在 Model API 暴露；若否，在 `ForumMeController`（或同前缀控制器）实现 GET，调用 `ForumEngagementService.ListFavoritePostsAsync`，映射为与公开帖子列表一致的 `ForumPostListItem` / `PagedPostsDto` 合同
- [x] 1.2 为 `me/favorites` 补充或扩展集成测试：未登录 401、分页合法与非法、已删帖不出现在返回项（与孤儿清理行为一致时可接受与 `totalCount` 的已知差异，并在测试注释中说明）

## 2. 前端 API 与类型

- [x] 2.1 在 `frontend/src/api/clients.ts`（或项目约定的 model 客户端）新增 `listMyForumPosts`、`listMyForumReplies`、`listMyForumFavorites`，分别请求 `GET /forum/me/posts`、`GET /forum/me/replies`、`GET /forum/me/favorites`，分页参数与现有 `listForumPosts` 对齐
- [x] 2.2 复用或对齐 `ForumPostListItem` / `PagedForumPosts` 类型；回复列表使用后端返回的 DTO 类型定义 TypeScript 接口

## 3. 路由与布局

- [x] 3.1 在 `frontend/src/router/index.ts` 注册 `/me` 父路由及子路由（posts / replies / favorites / settings），均带 `requiresAuth: true`；默认重定向到 `posts` 或 `favorites` 之一并在 `design` 中保持一致
- [x] 3.2 新建个人中心布局组件：侧栏或 Tab 导航至各子视图，并提供指向 `/profile` 的「资料」入口

## 4. 视图与状态

- [x] 4.1 实现「我的帖子」视图：分页、加载中、空列表、请求失败提示；列表项复用首页帖子卡片或抽取的共享组件，点击进入帖子详情
- [x] 4.2 实现「我的回复」视图：分页与三态；每条展示回复摘要与进入所属帖子详情的链接（字段以后端为准）
- [x] 4.3 实现「我的收藏」视图：分页与三态；列表项与公开列表字段一致或可说明映射
- [x] 4.4 实现「设置」视图：提供退出登录（复用 auth store / 现有退出逻辑）；若壳层已有主题切换则接入，否则展示明确占位文案

## 5. 全局入口与验收

- [x] 5.1 在 `HeaderUserMenu.vue`（或等价位置）为已登录用户增加「个人中心」入口，指向 `/me`
- [x] 5.2 手工走查 pm-plan Issue #2 验收：换账号列表变化、`me/favorites` 匿名 401、删帖后收藏列表不含该帖、Profile 与 customer API 无冲突
  - 验收记录：请在合并 PR 时在描述中简要写明执行环境、日期与结论（或通过 Issue 评论链出），便于审计对照本项。
- [x] 5.3 运行前端类型检查/构建与相关 `dotnet test`（若改后端），确保 CI 通过
