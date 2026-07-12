# Tasks: frontend-data-access-layering

- [ ] T1: 新增 useCurrentUser() composable
- [ ] T2: 迁移 AdminTagsView → useAdminTags()
- [ ] T3: 清理 HomeView 直调（已有 4 个 composable，只需移除 1 处直调）
- [ ] T4: 迁移 ProfileView → useProfile()
- [ ] T5: 清理 MeDraftsView / MeFavoritesView / MePostsView / MeRepliesView 直调
- [ ] T6: 清理 ForumPostListCard 直调 api/clients
- [ ] T7: 清理 ForumPostGovernancePanel 直调 api/clients
- [ ] T8: 清理 AppHeader 直调 api/clients → useNotificationBadge()
- [ ] T9: 更新 frontend-app-shell spec 新增3条 requirement
- [ ] T10: 运行 npm test 全通过
