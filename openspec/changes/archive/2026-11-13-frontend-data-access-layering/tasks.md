# Tasks: frontend-data-access-layering

> 状态核对（2026-11-13）：本 change 的实现与规范在后续迭代中已全部落地，
> 但 tasks 状态停留在 2026-06-19 的 0/10，且从未补 delta spec，形成「已完成未归档」的僵尸 change。
> 现逐项复核后勾选，并以 `openspec archive --skip-specs` 归档（requirements 已在主规范，无需再合并 delta）。

- [x] T1: 新增 useCurrentUser() composable — `frontend/src/composables/useCurrentUser.ts` 已存在，被 10 个文件引用
- [x] T2: 迁移 AdminTagsView → useAdminTags() — 已改用 composable
- [x] T3: 清理 HomeView 直调 — 仅剩 `import type`（类型导入，非 API 调用）
- [x] T4: 迁移 ProfileView → useProfile() — 已改用 composable
- [x] T5: 清理 MeDraftsView / MeFavoritesView / MePostsView / MeRepliesView 直调 — 四个 `views/me/*` 均走对应 composable
- [x] T6: 清理 ForumPostListCard 直调 api/clients — 改用 `useCurrentUser()`，仅剩类型导入
- [x] T7: 清理 ForumPostGovernancePanel 直调 api/clients — 改用 `useCurrentUser()` + `usePostGovernance()`
- [x] T8: 清理 AppHeader 直调 api/clients → useNotificationBadge() — 已改用两个 composable
- [x] T9: 更新 frontend-app-shell spec 新增 3 条 requirement — 主规范第 247/256/265 行齐备
- [x] T10: 运行 npm test 全通过 — 2026-11-13 复核：12 个测试文件 / 55 项测试全绿
