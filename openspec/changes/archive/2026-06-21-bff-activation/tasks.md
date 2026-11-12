## 1. BFF 基础设施准备

- [x] 1.1 读取 User API `appsettings.Local.json` 中 `Jwt:RefreshTokenExpiryDays` 值，记录到 BFF `appsettings.Local.example.json` 的 `DownstreamServices:RefreshTokenExpiryDays` 配置项
- [x] 1.2 在 BFF `Program.cs` 中注册 `IConnectionMultiplexer`（StackExchange.Redis），从配置读取 Redis 连接串；在 BFF `.csproj` 中添加 `StackExchange.Redis` 包引用
- [x] 1.3 在 BFF `Program.cs` 中注册 ASP.NET Core 内建 Rate Limiter（`AddRateLimiter`），配置 `bff-auth` 策略：Fixed Window，60 秒 / 20 次，超限返回 429
- [x] 1.4 实现 `BffSourceHeaderMiddleware`：检查请求是否含 `X-BFF-Source: web` Header；仅对变更端点（POST/DELETE 且路径匹配 `/api/bff/auth/*`）生效；缺失时返回 400 并记录警告日志
- [x] 1.5 在 BFF `Program.cs` 中注册 `BffSourceHeaderMiddleware` 和 `UseRateLimiter`，确保在 `UseAuthentication` 之前执行来源校验

## 2. Phase 1 后端：Cookie Session 鉴权端点

- [x] 2.1 实现 `BffAuthController.Login`（`POST /api/bff/auth/login`）：接收 `{ email, password }`，转发调用 User API `POST /auth/login`，成功时写 Refresh Token Cookie（`HttpOnly; SameSite=Strict; Path=/api/bff/auth; Max-Age=<配置值>`；开发环境不加 `Secure`），响应体仅返回 `{ accessToken, accessTokenExpiresAtUtc }`
- [x] 2.2 实现 `BffAuthController.Token`（`GET /api/bff/auth/token`）：`[AllowAnonymous]`；读取 Cookie 中 Refresh Token；Redis 轻量锁（SET NX，key = `bff:refresh:<SHA256前16字节>，TTL=10s`）；调用 User API refresh；更新 Cookie；401 时清除 Cookie（`Max-Age=0`）；对此端点应用 `bff-auth` 限流策略
- [x] 2.3 实现 `BffAuthController.Refresh`（`POST /api/bff/auth/refresh`）：读取 Cookie Refresh Token；Redis 锁逻辑同 2.2；调用 User API refresh；更新 Cookie；Cookie 不存在时 401；对此端点应用 `bff-auth` 限流策略
- [x] 2.4 实现 `BffAuthController.Revoke`（`POST /api/bff/auth/revoke`）：读取 Cookie Refresh Token；若存在则调用 User API revoke；无论成功失败均以 `Max-Age=0` 清除 Cookie；Cookie 不存在时直接 200（幂等）
- [x] 2.5 引入 `WireMock.Net` 作为 BFF 集成测试的下游 mock 框架：在 BFF 测试项目中配置 `WireMockServer`，固定 User API / Customer API / Model API 响应结构（版本化 stub）；当下游 DTO 结构变更时，BFF 集成测试失败作为第一道告警
- [x] 2.6 为 Phase 1 编写集成测试（WebApplicationFactory + WireMock stub）：覆盖 Login 成功/密码错误、Token 有效/过期/无 Cookie、Refresh 成功/无 Cookie、Revoke 幂等、缺少 X-BFF-Source 返回 400、Rate Limit 触发返回 429

## 3. Phase 1 前端：auth.ts 与 clients.ts 重构

- [x] 3.1 修改 `auth.ts`：删除 `setToken` 中的 `localStorage.setItem`，删除 `setRefreshToken` 及其 localStorage 路径；`token` ref 仅维护内存值；`refreshToken` ref 移除（不再需要，BFF Cookie 持有）；`clearAuth` 只清 Pinia store 不清 localStorage
- [x] 3.2 在 `auth.ts` 中新增 `restoreSession()` 函数：调用 `GET /api/bff/auth/token`（通过 `bffApi`）；成功时调用 `applyAuthSession(accessToken)`；401/失败时调用 `clearAuth()`（不跳转）
- [x] 3.3 在 `auth.ts` 中新增 `BroadcastChannel('jissweb.auth')` 监听：收到 `{ type: 'token-refreshed', accessToken }` 消息时更新 Pinia store；在 `applyAuthSession` 中广播该消息
- [x] 3.4 修改 `clients.ts` 的 `runSingleFlightRefresh`：目标端点改为 `POST /api/bff/auth/refresh`（请求体为空，BFF 从 Cookie 读取 Refresh Token）；请求头加 `X-BFF-Source: web`；响应结构改为 `{ accessToken, accessTokenExpiresAtUtc }`（无 refreshToken 字段）
- [x] 3.5 修改登录页面调用：`login()` 函数改调 `/api/bff/auth/login`（通过 `bffApi`）；请求头加 `X-BFF-Source: web`；从响应中取 `accessToken` 写入 Pinia store（不再处理 refreshToken）
- [x] 3.6 修改注销逻辑：调用 `POST /api/bff/auth/revoke`（通过 `bffApi`，加 `X-BFF-Source: web` Header）；完成后调用 `clearAuth()`
- [x] 3.7 在 `App.vue`（或 router 全局 beforeEach 初始化逻辑）中调用 `restoreSession()`，确保页面刷新后静默恢复登录态
- [x] 3.8 为重构后的 `auth.ts` 添加 Vitest 测试：覆盖 restoreSession 成功/401、BroadcastChannel 广播与接收、clearAuth 不写 localStorage

## 4. Phase 2 后端：Me Bundle

- [x] 4.1 实现 `BffMeController.Get`（`GET /api/bff/me`）：`[Authorize]`；并发调用 Customer API `GET /api/profile` 和 Model API `GET /api/forum/notifications/unread-count`，均透传请求中的 Bearer Token；任一失败时降级（字段 null，追加 `warnings`）；成功时返回 `{ profile, forum: { unreadCount } }`
- [x] 4.2 为 `/api/bff/me` 编写集成测试：覆盖已登录聚合成功、未登录 401、Customer API 超时时降级返回、Model API 超时时降级返回

## 5. Phase 2 前端：导航栏改用 Me Bundle

- [x] 5.1 在 `frontend/src/api/clients.ts` 中新增 `getMe()` 函数，调用 `GET /api/bff/me`（通过 `bffApi`），返回 `{ profile, forum }` 结构
- [x] 5.2 修改导航栏相关 composable（如 `useCurrentUser`），将原先对 Customer API profile + Model API unread-count 的两次独立调用合并为一次 `getMe()` 调用
- [x] 5.3 验证导航栏在已登录/未登录/降级（warnings 存在）三种状态下表现正确

## 6. Phase 3 后端：Forum Init Bundle

- [x] 6.1 实现 `BffForumInitController.Get`（`GET /api/bff/forum-init`）：接受 `page`、`pageSize`、`boardId` 查询参数；`[AllowAnonymous]`；并发调用 4 个 Model API 只读端点；已登录时额外调用 unread-count（读取 Authorization header 判断）；局部失败降级；返回 `{ boards, announcements, posts, popularTags, unreadCount, warnings? }`
- [x] 6.2 为 `/api/bff/forum-init` 编写集成测试：覆盖匿名请求（unreadCount=0）、登录请求（含 unread-count）、boardId 透传、某下游超时时降级、Method Not Allowed

## 7. Phase 3 前端：论坛首页改用 Forum Init Bundle

- [x] 7.1 在 `frontend/src/api/clients.ts` 中新增 `getForumInit()` 函数，调用 `GET /api/bff/forum-init`，返回聚合结构并附加类型定义
- [x] 7.2 修改论坛首页（`ForumListView` 或等效组件）的 `onMounted`：将对 boards、announcements、posts、popularTags、unread-count 的分散调用替换为单次 `getForumInit()` 调用；处理 `warnings` 字段做局部降级提示
- [x] 7.3 为 `getForumInit()` 添加 Vitest 测试：覆盖正常响应解析、warnings 字段处理

## 8. 收尾与验证

- [x] 8.1 添加 Vite 代理配置：在 `vite.config.ts` 中确认 `/api/bff` 路径已由 `/api` 通配规则覆盖（应已覆盖，无需新增；需验证 `bffApi.get('/bff/...')` 实际走 Gateway → BFF 的链路）
- [x] 8.2 运行全量后端测试：`dotnet test backend/tests/JIssWeb.Frontend.Bff.Tests`，确认无回归（20/20 passed）
- [x] 8.3 运行全量前端测试：`cd frontend && npm test`，确认无回归（31/31 passed）
- [x] 8.4 手动验证黄金路径：登录 → 刷新页面（静默恢复）→ 打开第二个标签页（BroadcastChannel）→ 注销；论坛首页加载仅发起 1 次 `/api/bff/forum-init` 请求（Network 面板确认）
- [x] 8.5 确认 localStorage 中 `jissweb.jwt` 和 `jissweb.refresh.local` 键在登录后不存在（auth.ts 仅做 removeItem，applyAuthSession 不写 localStorage；auth.test.ts 已覆盖此断言）
- [x] 8.6 检查所有 BffController 中是否存在业务条件判断（如帖子状态判断、权限规则）：grep `Controllers/Bff` 确认无领域逻辑；若存在则提 issue 下沉到对应领域服务
