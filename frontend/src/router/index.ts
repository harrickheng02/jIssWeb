import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore, bcSyncReady } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('../views/HomeView.vue'),
    },
    {
      path: '/posts/:id',
      name: 'post-detail',
      component: () => import('../views/PostDetailView.vue'),
    },
    {
      path: '/moderation',
      component: () => import('../views/moderation/ModerationLayout.vue'),
      meta: { requiresAuth: true, requiresModerate: true },
      redirect: { name: 'moderation-audit' },
      children: [
        {
          path: 'audit',
          name: 'moderation-audit',
          meta: { requiresAuth: true, requiresModerate: true },
          component: () => import('../views/ModerationAuditFeedView.vue'),
        },
        {
          path: 'reports',
          name: 'moderation-reports',
          meta: { requiresAuth: true, requiresModerate: true },
          component: () => import('../views/ModerationReportsQueueView.vue'),
        },
        {
          path: 'tags',
          name: 'moderation-tags',
          meta: { requiresAuth: true, requiresModerate: true },
          component: () => import('../views/AdminTagsView.vue'),
        },
      ],
    },
    {
      path: '/auth',
      name: 'auth',
      meta: { hideAppShell: true },
      component: () => import('../views/AuthView.vue'),
    },
    {
      path: '/auth/forgot',
      name: 'auth-forgot',
      meta: { hideAppShell: true },
      component: () => import('../views/ForgotPasswordView.vue'),
    },
    {
      path: '/auth/reset',
      name: 'auth-reset',
      meta: { hideAppShell: true },
      component: () => import('../views/ResetPasswordView.vue'),
    },
    {
      path: '/customers',
      name: 'customers',
      component: () => import('../views/CustomersView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/profile',
      name: 'profile',
      redirect: '/me/profile',
      meta: { requiresAuth: true },
    },
    {
      path: '/me',
      component: () => import('../views/me/PersonalCenterLayout.vue'),
      meta: { requiresAuth: true },
      redirect: '/me/posts',
      children: [
        {
          path: 'profile',
          name: 'me-profile',
          meta: { requiresAuth: true },
          component: () => import('../views/ProfileView.vue'),
        },
        {
          path: 'posts',
          name: 'me-posts',
          meta: { requiresAuth: true },
          component: () => import('../views/me/MePostsView.vue'),
        },
        {
          path: 'replies',
          name: 'me-replies',
          meta: { requiresAuth: true },
          component: () => import('../views/me/MeRepliesView.vue'),
        },
        {
          path: 'favorites',
          name: 'me-favorites',
          meta: { requiresAuth: true },
          component: () => import('../views/me/MeFavoritesView.vue'),
        },
        {
          path: 'drafts',
          name: 'me-drafts',
          meta: { requiresAuth: true },
          component: () => import('../views/me/MeDraftsView.vue'),
        },
        {
          path: 'settings',
          name: 'me-settings',
          meta: { requiresAuth: true },
          component: () => import('../views/me/MeSettingsView.vue'),
        },
      ],
    },
    {
      path: '/notifications',
      name: 'notifications',
      component: () => import('../views/NotificationsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/register/pending',
      name: 'register-pending',
      component: () => import('../views/RegisterPendingView.vue'),
    },
    {
      path: '/register/verified',
      name: 'register-verified',
      component: () => import('../views/RegisterVerifiedView.vue'),
    },
    {
      path: '/admin/tags',
      redirect: { name: 'moderation-tags' },
    },
  ],
})

router.beforeEach(async (to) => {
  await bcSyncReady
  const auth = useAuthStore()
  if (to.meta.requiresModerate && !auth.canModerate) return { name: 'home' }
  if (!to.meta.requiresAuth) return true
  if (auth.token) return true
  return { name: 'auth' }
})

export default router
