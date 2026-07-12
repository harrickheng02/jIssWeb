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
import { useThemeStore } from './stores/theme'
import { useAuthStore } from './stores/auth'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
useThemeStore(pinia)

// 仅在曾经登录过时才静默刷新（未登录用户跳过，避免无意义的 401）
// token 非空 = 同 tab 刷新；hint = 关闭所有 tab 后重新打开（Cookie 可能还有效）
const authStore = useAuthStore(pinia)
if (authStore.token || localStorage.getItem('jissweb.session.hint')) {
  void authStore.restoreSession()
}

app.use(router)
app.use(ElementPlus, { locale: zhCn })

app.mount('#app')
