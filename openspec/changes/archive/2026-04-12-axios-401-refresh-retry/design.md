## 背景

`frontend/src/api/clients.ts` 中 `createClient` 仅有 **请求** 拦截器，从 `useAuthStore()` 设置 `Authorization: Bearer`。多个具名 axios 实例共用 `/api` 基址。`main.ts` 在存在 refresh token 时已在启动调用 `refresh(refreshToken)`。**无** 响应拦截器；会话中途 access 过期时，调用方直接收到 401，不会尝试 refresh。

## 目标 / 非目标

**目标：**

- 对已使用 Bearer 的出站请求，若响应为 HTTP **401**，则协调执行 **一次** refresh，并用新 access token **重试原请求一次**。
- **单飞（single-flight）refresh**：同一 `prefix` 下并发 401 共用一个进行中的 refresh Promise；不同 `prefix` 分桶。
- refresh 不可行或失败时 **清空会话并跳转登录**，与启动 refresh 失败时 `clearAuth()` 一致。
- 在 **`createClient`** 内实现，使各实例行为一致。

**非目标：**

- 无 refresh token 时的静默重新登录（pm-plan 已列为非目标）。
- 修改后端 JWT 有效期或 refresh API 形态。
- 对同一原始请求因 401 重试超过一次。
- Gitee 同步脚本或非浏览器客户端。

## 决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| 挂载位置 | `createClient` 响应拦截器 | 单点维护；各 `*Api` 实例对齐。 |
| refresh 的 HTTP 调用 | 按该 axios 实例的 baseURL（prefix）拼接 `/auth/refresh`；多 prefix 时单飞按 prefix 分桶。使用 **原生 `axios.post`** | 与 `createClient(prefix)` 对齐；refresh 不经带 401 拦截的实例，避免递归。 |
| 并发 | 同 prefix 内共用一个进行中的 refresh Promise；不同 prefix 各自单飞 | 符合 pm-plan「串行或单例」。 |
| 重试上限 | 请求 `config` 标记（如 `_retryAfterRefresh`）：**最多** 重试一次 | 防止死循环。 |
| 鉴权路由 | URL 匹配 `/auth/refresh`、`/auth/login`、`/auth/register` 等时跳过 401→refresh | 防循环；refresh 失败仍清会话。 |
| 失败后 UX | `auth.clearAuth()` 与 `router.push('/auth')` 或等价集中封装 | 与 `main.ts` 中 `.catch(() => auth.clearAuth())` 一致；注意 `clients.ts` 与 router 循环依赖（懒加载 `import()` 或回调）。 |

**曾考虑：** 仅保留全局单一 axios 实例——对象更少但改动面大，暂缓。按客户端分别挂拦截器——重复维护风险高。

## 风险与权衡

| 风险 | 缓解 |
|------|------|
| 循环依赖：`clients.ts` ↔ `router` / `stores` | 路由跳转用动态 `import()`，或由 `main` 挂载后注入回调；请求拦截器已用 Pinia store。 |
| 401 非过期导致（如吊销） | refresh 失败→与现网一致：清会话+登录；可接受。 |
| 启动 refresh 与首屏立即 401 叠加 | 启动 refresh 先于挂载更新 token；极端竞态由单飞覆盖。 |

## 迁移与回滚

仅部署前端；无数据库或 API 版本迁移。回滚：撤销拦截器相关提交。

## 待决问题

- 是否将「跳转登录」收到小模块 `authRedirect.ts`，以减少在 `clients.ts` 中直接 `import` router（实现阶段确定）。
