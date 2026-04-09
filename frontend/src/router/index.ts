import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('../views/HomeView.vue'),
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
      path: '/customers',
      name: 'customers',
      component: () => import('../views/CustomersView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/profile',
      name: 'profile',
      component: () => import('../views/ProfileView.vue'),
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
  ],
})

router.beforeEach((to) => {
  if (!to.meta.requiresAuth) return true
  const auth = useAuthStore()
  if (auth.token) return true
  return { name: 'auth' }
})

export default router
