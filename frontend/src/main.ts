import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import App from './App.vue'
import router from './router'
import { refresh } from './api/clients'
import { useAuthStore } from './stores/auth'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(router)
app.use(ElementPlus)

const auth = useAuthStore(pinia)
if (auth.refreshToken) {
  refresh(auth.refreshToken)
    .then((res) => {
      if (res.success && res.data) auth.applyAuthSession(res.data.accessToken, res.data.refreshToken, auth.rememberMe)
      else auth.clearAuth()
    })
    .catch(() => auth.clearAuth())
    .finally(() => app.mount('#app'))
} else {
  app.mount('#app')
}
