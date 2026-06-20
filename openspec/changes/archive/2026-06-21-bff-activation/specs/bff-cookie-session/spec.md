## ADDED Requirements

### Requirement: BFF 提供 Cookie Session 登录端点

BFF SHALL 提供 `POST /api/bff/auth/login` 端点，接收 `{ email, password }` JSON 请求体，代理调用 User API 完成鉴权，将 Refresh Token 写入 HttpOnly Cookie，并在响应体中返回 Access Token 及其过期时间。

#### Scenario: 登录成功

- **WHEN** 客户端 POST `/api/bff/auth/login` 并携带有效 `{ email, password }`，且请求头含 `X-BFF-Source: web`
- **THEN** BFF SHALL 调用 User API 登录，将 Refresh Token 存入 `HttpOnly; SameSite=Strict; Secure; Path=/api/bff/auth; Max-Age=<对齐 User API 配置>` Cookie，并返回 `{ success: true, data: { accessToken, accessTokenExpiresAtUtc } }` 响应体（不含 refreshToken 字段）

#### Scenario: 缺少 X-BFF-Source Header 被拒绝

- **WHEN** 客户端 POST `/api/bff/auth/login` 但请求头不含 `X-BFF-Source: web`
- **THEN** BFF SHALL 返回 HTTP 400，不调用 User API，不写入 Cookie

#### Scenario: 凭证错误时透传错误

- **WHEN** 客户端 POST `/api/bff/auth/login` 携带错误密码
- **THEN** BFF SHALL 将 User API 返回的错误透传给客户端（包含 `success: false` 和原始 `message`/`code`），不写入任何 Cookie

---

### Requirement: BFF 提供 Cookie Session 静默恢复端点

BFF SHALL 提供 `GET /api/bff/auth/token` 端点，不要求 Bearer Token，仅依赖 HttpOnly Cookie 中的 Refresh Token 向 User API 续期，并返回新 Access Token。

#### Scenario: Cookie 有效时静默恢复成功

- **WHEN** 客户端 GET `/api/bff/auth/token` 并自动携带有效 Refresh Token Cookie
- **THEN** BFF SHALL 向 User API 请求续期，更新 Cookie（重置 Max-Age），并返回 `{ success: true, data: { accessToken, accessTokenExpiresAtUtc } }`

#### Scenario: Cookie 不存在或已过期

- **WHEN** 客户端 GET `/api/bff/auth/token` 时 Cookie 不存在或 Refresh Token 已过期
- **THEN** BFF SHALL 返回 HTTP 401，并在响应头中以 `Max-Age=0` 清除残留 Cookie

#### Scenario: 并发续期时 Redis 锁保护

- **WHEN** 多个请求同时 GET `/api/bff/auth/token` 携带同一 Refresh Token Cookie
- **THEN** BFF SHALL 对相同 Refresh Token（使用其 SHA-256 前缀作为 Redis key，TTL 10 秒）施加分布式锁，确保同一 Token 同时只有一次实际调用 User API；后续请求等待锁释放后复用结果

---

### Requirement: BFF 提供 Cookie Session 续期端点

BFF SHALL 提供 `POST /api/bff/auth/refresh` 端点，从 Cookie 读取 Refresh Token，向 User API 请求续期，并更新 Cookie 和返回新 Access Token。

#### Scenario: 续期成功

- **WHEN** 客户端 POST `/api/bff/auth/refresh` 并携带有效 Refresh Token Cookie 及 `X-BFF-Source: web` Header
- **THEN** BFF SHALL 完成续期，更新 Cookie，并返回 `{ success: true, data: { accessToken, accessTokenExpiresAtUtc } }`

#### Scenario: Cookie 不存在时返回 401

- **WHEN** 客户端 POST `/api/bff/auth/refresh` 但 Cookie 中无 Refresh Token
- **THEN** BFF SHALL 返回 HTTP 401，不调用 User API

---

### Requirement: BFF 提供 Cookie Session 撤销端点

BFF SHALL 提供 `POST /api/bff/auth/revoke` 端点，从 Cookie 读取 Refresh Token，代理调用 User API 撤销，并清除 Cookie。

#### Scenario: 登出成功

- **WHEN** 客户端 POST `/api/bff/auth/revoke` 并携带 Refresh Token Cookie 及 `X-BFF-Source: web` Header
- **THEN** BFF SHALL 调用 User API revoke，并在响应头中以 `Max-Age=0` 清除 Cookie，返回 `{ success: true }`

#### Scenario: 无 Cookie 时幂等清除

- **WHEN** 客户端 POST `/api/bff/auth/revoke` 但 Cookie 中无 Refresh Token
- **THEN** BFF SHALL 直接返回 `{ success: true }`，不调用 User API（幂等）

---

### Requirement: Cookie Session 端点的请求来源校验

所有变更类 BFF 鉴权端点（`/api/bff/auth/login`、`/api/bff/auth/refresh`、`/api/bff/auth/revoke`）SHALL 要求请求携带 `X-BFF-Source: web` 自定义 Header。

#### Scenario: 缺少自定义 Header 时拒绝

- **WHEN** 任意变更类端点收到的请求不含 `X-BFF-Source: web` Header
- **THEN** BFF SHALL 返回 HTTP 400，拒绝处理

---

### Requirement: 前端移除 localStorage Token 存储

前端 `auth.ts` SHALL 不再将 Access Token 或 Refresh Token 写入 `localStorage` 或 `sessionStorage`；Access Token 仅以 Pinia `ref<string | null>` 存活于内存。

#### Scenario: 页面刷新后静默恢复

- **WHEN** 用户刷新页面，App 初始化时 Pinia store 中无 Access Token
- **THEN** 前端 SHALL 调用 `GET /api/bff/auth/token` 静默恢复登录态；成功时将返回的 Access Token 写入 Pinia store；401 时保持未登录状态，不自动跳转

#### Scenario: 多标签页续期协调

- **WHEN** 用户同时打开多个标签页，某个标签页完成 Token 续期
- **THEN** 该标签页 SHALL 通过 `BroadcastChannel` 将新 Access Token 广播给同源其他标签页，其他标签页更新 Pinia store，不再各自发起续期请求

---

### Requirement: BFF 鉴权端点请求频率限制

`GET /api/bff/auth/token` 和 `POST /api/bff/auth/refresh` SHALL 对同一 IP 实施请求频率限制，防止暴力枚举。

#### Scenario: 超出频率限制

- **WHEN** 同一 IP 在 60 秒内对上述端点的请求次数超过配置阈值（默认 20 次/分钟）
- **THEN** BFF SHALL 返回 HTTP 429，不调用 User API
