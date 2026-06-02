import './styles/theme-init'
import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import './styles/forum-tokens.css'
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import App from './App.vue'
import router from './router'
import { refresh } from './api/clients'
import { useAuthStore } from './stores/auth'
import { useThemeStore } from './stores/theme'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
useThemeStore(pinia)
app.use(router)
app.use(ElementPlus, { locale: zhCn })

const auth = useAuthStore(pinia)

function mountApp() {
  app.mount('#app')
  if (!import.meta.env.DEV) return
  const w = window as Window & { __jissExpireAccess?: () => void; __jissInvalidateRefresh?: () => void }
  w.__jissExpireAccess = () => {
    useAuthStore(pinia).setToken(
      'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkZXYifQ.fake',
    )
  }
  w.__jissInvalidateRefresh = () => {
    useAuthStore(pinia).setRefreshToken('invalid')
  }
}

if (auth.refreshToken) {
  refresh(auth.refreshToken)
    .then((res) => {
      if (res.success && res.data) auth.applyAuthSession(res.data.accessToken, res.data.refreshToken)
      else auth.clearAuth()
    })
    .catch(() => auth.clearAuth())
    .finally(() => mountApp())
} else {
  mountApp()
}
