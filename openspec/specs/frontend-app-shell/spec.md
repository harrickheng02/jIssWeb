## Purpose

Vue 3 SPA: dev proxy to backends, auth client, customer/profile flows, and verification UX.
## Requirements
### Requirement: SPA shell with router and state

The frontend SHALL use Vue 3 with TypeScript, Vite, Pinia, vue-router, and Element Plus, and SHALL mount a root layout that renders routed views.

#### Scenario: Dev server starts

- **WHEN** the developer runs the frontend dev script
- **THEN** the application SHALL load without runtime errors in the default view

### Requirement: Multi-service proxy configuration

The Vite development server SHALL define proxy rules mapping distinct path prefixes to each backend service base URL (ports per design), so that browser calls avoid CORS for same-origin paths during development.

#### Scenario: Proxy to user service

- **WHEN** the SPA requests a resource under the user-service proxy prefix
- **THEN** the request SHALL be forwarded to the user service host and port configured in `vite.config`

### Requirement: Authenticated API client

The frontend SHALL provide an HTTP client (axios or equivalent) that attaches `Authorization: Bearer <token>` when a token is present in application state, for calls to protected APIs.

#### Scenario: Token attached

- **WHEN** a token is stored after login placeholder flow and a protected API is invoked
- **THEN** the outgoing request SHALL include the Bearer header

### Requirement: Surface for service smoke checks

The frontend SHALL include at least one view or dev-only panel that can trigger requests to each backend health endpoint (or document manual steps in tasks if automated UI is deferred).

#### Scenario: Manual verification path

- **WHEN** a developer follows the tasks document to open the smoke view or run listed URLs
- **THEN** they SHALL be able to confirm each service responds successfully

### Requirement: Customer records UI entry

The frontend SHALL provide a routed view or section that performs authenticated calls to the customer service CRUD API (via the configured proxy prefix), including at least list and create (or equivalent minimal flow) to validate the login-to-customer pipeline.

#### Scenario: Authenticated customer list request

- **WHEN** a logged-in user opens the customer records view
- **THEN** the application SHALL request the customer list endpoint with `Authorization: Bearer` when a token is present

#### Scenario: Unauthenticated user cannot load protected customer data

- **WHEN** no token is present and the user attempts the same protected action
- **THEN** the UI SHALL avoid sending the request or SHALL handle 401 without exposing other users' data

### Requirement: Email verification routes

The SPA SHALL provide routed views for the email verification success path and for the pending-verification state that only allows resending verification email and static guidance, consistent with `email-verification-registration`. On successful verification, the SPA SHALL integrate returned session credentials the same way as after login so the user does not need a separate password entry step.

#### Scenario: Success view after verification

- **WHEN** the user completes verification via the backend flow and lands on the SPA success route with session credentials available per `email-verification-registration`
- **THEN** the UI SHALL persist tokens or equivalent credentials using the same client storage and auth state path as the login success flow
- **AND** the UI SHALL present a confirmation state and SHALL navigate to an authenticated destination without requiring navigation to the login password form solely to obtain a session

#### Scenario: Authenticated after verify without redundant login

- **WHEN** access and refresh tokens (or equivalent) are present after verification completion
- **THEN** subsequent navigation SHALL honor the same route guards as a post-login session for protected shell features allowed for verified users

### Requirement: Remember-me and silent refresh

The SPA SHALL support a user-controlled option to persist refresh tokens for automatic re-authentication on subsequent visits and SHALL attempt silent token refresh on startup when persisted credentials exist, as documented in `email-verification-profile-auth` design.

#### Scenario: Startup refresh attempt

- **WHEN** a refresh token is present in the chosen storage and remember-me semantics apply
- **THEN** the application SHALL request token refresh without requiring password entry on first load

### Requirement: Unverified users cannot access protected shell features

Until the account is verified, the SPA SHALL route users to the pending-verification experience and SHALL NOT call protected customer or profile APIs except where explicitly allowed by the user service for resend operations.

#### Scenario: Guarded navigation

- **WHEN** a logged-in user is unverified (if the client can detect this state)
- **THEN** the UI SHALL block or redirect away from protected business views until verification completes

### Requirement: Frontend uses unified backend entry

The frontend SHALL progressively use a unified backend entry domain or path model instead of directly depending on per-service development prefixes for long-term architecture.

#### Scenario: Frontend does not need downstream topology
- **WHEN** the SPA performs authenticated or domain API calls
- **THEN** the request model SHALL not require the browser code to know the concrete host or permanent public prefix of each backend service

### Requirement: Gateway or BFF aware client configuration

The frontend client configuration SHALL support routing requests through the gateway and, where needed, BFF endpoints, while preserving authenticated request behavior.

#### Scenario: Token still attached through unified entry
- **WHEN** a protected request is sent through the unified backend entry
- **THEN** the outgoing request SHALL still include the Bearer token when one is present in application state

### Requirement: Unified auth page shell

The frontend SHALL provide a unified authentication page shell that groups branding/header content, login or registration form content, state switching controls, and footer links into a consistent routed experience.

#### Scenario: Auth shell presents a single entry page
- **WHEN** a user lands on the authentication route
- **THEN** the UI SHALL present login and registration as coordinated states of the same page shell

### Requirement: Inline validation and request feedback

The frontend SHALL display inline field-level validation for authentication inputs and SHALL expose request-level feedback for server-side failures such as wrong password, missing account, invalid verification code, throttling, or captcha escalation when provided by the backend.

#### Scenario: Server error displayed clearly
- **WHEN** an authentication request fails with a known backend error code
- **THEN** the UI SHALL show an understandable error message without forcing the user to infer what went wrong

### Requirement: Authentication form affordances

The frontend SHALL support password visibility toggles, remember-me selection, forgot-password entry placement, agreement confirmation for registration, and loading or disabled submit buttons consistent with `auth-page-experience`.

#### Scenario: Registration requires agreement
- **WHEN** the user attempts registration without confirming the required agreement checkbox
- **THEN** the UI SHALL block submission and present clear feedback

### Requirement: Root route serves the forum homepage

The frontend SHALL use the root route as the default forum homepage entry and SHALL keep authentication available on a dedicated routed page.

#### Scenario: Root route loads content shell

- **WHEN** the application resolves the root route
- **THEN** it SHALL render the forum homepage view

#### Scenario: Authentication remains available

- **WHEN** a user navigates to the authentication route
- **THEN** the application SHALL render the unified login and registration page shell

### Requirement: Existing protected routes remain guarded after homepage split

The frontend SHALL preserve route-guard behavior for protected pages after moving authentication away from the root route.

#### Scenario: Unauthenticated user opens protected page

- **WHEN** a user without a token navigates to a protected route such as customer or profile pages
- **THEN** the router SHALL block access and redirect to the authentication route instead of rendering protected content

### Requirement: 忘记密码与重置密码路由

SPA SHALL 提供路由页面：其一用于提交邮箱以请求密码重置，其二用于通过后端流程交付的重置凭据（链接、重定向或约定的 token 传递）完成密码重置。实现本能力后，忘记密码页面 SHALL 不得仍为静态「不可用」占位。

#### Scenario: 从认证壳进入忘记密码

- **WHEN** 用户从认证页进入忘记密码路由
- **THEN** 界面 SHALL 提供提交邮箱并请求重置的表单，并具备加载中与错误反馈

#### Scenario: 点击邮件链接后完成重置

- **WHEN** 用户按 user-service 设计携带凭据或 exchange 码进入重置路由
- **THEN** 界面 SHALL 允许输入并确认新密码，并向完成端点提交

### Requirement: 密码重置成功与会话集成方式与登录一致

密码重置成功后，SPA SHALL 使用与登录成功相同的存储与认证状态路径持久化 access 与 refresh（或等价凭据），并 SHALL 导航至已登录目的地，且 SHALL 不要求用户仅为获得会话而再次执行登录步骤。

#### Scenario: 重置后无需再登录

- **WHEN** 密码重置完成响应包含与 `password-reset-email` 及 `user-service` 一致的会话凭据
- **THEN** 应用 SHALL 按登录成功方式更新认证状态
- **AND** 后续导航 SHALL 对已验证用户适用与登录后会话相同的路由守卫

### Requirement: 401 响应触发 refresh 且仅重试原请求一次

SPA 的 HTTP 客户端在同时满足以下条件时必须处理响应：HTTP 状态为 **401**、请求曾携带 `Authorization: Bearer` access token、应用状态中仍存在 refresh token。此时客户端必须经既有 user-service refresh 端点获取新的 access token（若返回轮换的 refresh token 则一并更新），并以与登录成功或启动 refresh 成功相同的方式持久化会话，且必须仅重试失败请求 **一次**（使用新 access token）。客户端不得进入无限 refresh 循环：refresh 请求及其他鉴权提交类端点不得以同一失败链再次触发本 401 处理逻辑。

#### Scenario: access 过期且 refresh 仍有效

- **当** 受保护 API 返回 401 且本地存有 refresh token
- **则** 客户端必须调用 token refresh，成功则更新会话，并以新 Bearer 重试原请求一次
- **且** 若重试成功，调用方必须能观察到成功结果

#### Scenario: refresh 失败或无 refresh token

- **当** 受保护请求返回 401 且无法 refresh 或 refresh 失败
- **则** 应用必须按启动 refresh 失败时的同样规则清空鉴权状态
- **且** 用户必须被引导至登录路由或等效的未登录入口

### Requirement: 并发 401 合并为单次 refresh

当多个在途请求并行收到 401 时，SPA 对该波失败至多执行 **一次** refresh；并发调用方必须在重试各自请求前等待同一 refresh 结果。

#### Scenario: access 过期后并行多个受保护请求

- **当** 两个及以上已鉴权请求同时因 401 失败且存在 refresh token
- **则** 客户端在重试前必须只发出一条 refresh 请求

### Requirement: 与启动时静默 refresh 一致

由 401 触发的 refresh 必须使用与启动 refresh、登录成功相同的 Pinia 鉴权 store 方法与存储键持久化 token，使「首次加载」与「会话中途过期」的 access/refresh 处理路径一致。

#### Scenario: 会话中途 refresh 成功后的会话形态

- **当** 由 401 驱动的 refresh 成功
- **则** 持久化凭据与 store 状态必须符合与 `main.ts` 启动 refresh 成功相同的规则

