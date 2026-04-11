## 1. HTTP 客户端：401 / refresh / 重试

- [x] 1.1 增加模块级单飞 refresh（共享 Promise）及请求 config，限制每条原始请求最多重试一次
- [x] 1.2 用原生 axios 实现 `POST /auth/refresh`（避免 401 递归）；成功时对返回的令牌对调用 `useAuthStore().applyAuthSession`（或等价方法）
- [x] 1.3 在 `createClient` 增加响应拦截器：401 且曾有 Bearer + 有 refresh token 时，等待单飞 refresh 后重试一次；对 `/auth/refresh`、`/auth/login`、`/auth/register` 等按设计排除
- [x] 1.4 refresh 失败或 401 后无 refresh token：执行 `clearAuth()` 并跳转 `/auth`（用懒加载 router 或小辅助函数避免循环依赖）

## 2. 验证

- [x] 2.0 自动化：`frontend` 下执行 `npm test`（`clients.401.test.ts`，MSW 模拟 401 / refresh / 并行）。
