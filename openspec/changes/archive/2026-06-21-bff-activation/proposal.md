## Why

`JIssWeb.Frontend.Bff` 已搭建但从未真正投入使用：前端 JWT 直存 `localStorage`（XSS 可窃取），首页需要 4–5 个并发请求才能完成初始化，用户状态需跨两个服务分别获取。BFF 恰好可以系统性解决这三个问题。

## What Changes

- **新增 Cookie Session（Token Handler Pattern）**：BFF 承接登录/刷新/撤销，将 Refresh Token 存入 `HttpOnly; SameSite=Strict; Secure` Cookie，Access Token 仅在内存（Pinia）存活，从根本上阻断 XSS 窃取长效凭证的路径。
- **新增 Me Bundle**：`GET /api/bff/me` 在 BFF 侧并发调用 Customer API（profile）+ Model API（未读数/论坛角色），前端导航栏从两次 HTTP 变为一次。
- **新增 Forum Init Bundle**：`GET /api/bff/forum-init` 在 BFF 侧并发拉取板块列表、公告、首页帖子、热门标签、（登录态）未读数，将论坛首页 TTI 所需的多次瀑布请求合并为单次往返。
- **前端 auth 流程重构**：移除 `localStorage` JWT 存储，App 启动时通过 `GET /api/bff/auth/token` 静默恢复登录态，刷新逻辑由 `BroadcastChannel`（多标签协调）+ BFF 侧 Redis 轻量锁（兜底）保护。
- **CSRF 防护**：所有 BFF 变更端点要求客户端附带 `X-BFF-Source: web` 自定义 Header，BFF 中间件校验；配合 `SameSite=Strict` 构成双层防护。

## Non-goals

- 不将论坛 mutation 类请求（点赞/回复/举报/版主操作）迁移到 BFF 代理——这些继续由前端携带内存 Bearer 直打下游服务。
- 不实现全站 BFF 全代理模式；BFF 仅负责鉴权代理与聚合端点。
- 不引入 ASP.NET Core Antiforgery Token——当前 SPA+JSON 场景下 Custom Header + SameSite=Strict 已足够。
- 不修改 User API 的 Refresh Token 有效期或签发逻辑——BFF 透传并对齐现有配置。

## Capabilities

### New Capabilities

- `bff-cookie-session`: BFF 侧 Token Handler——登录/刷新/撤销/静默恢复的 HttpOnly Cookie 会话管理
- `bff-page-init-bundles`: BFF 侧页面数据聚合——Me Bundle（用户状态）与 Forum Init Bundle（论坛首页）

### Modified Capabilities

- `frontend-bff-service`: 在现有"BFF 可做聚合"的骨架上补充 Cookie Session 与具体聚合端点的行为契约

## Impact

- **服务边界**：JIssWeb.Frontend.Bff（主要新增）；Gateway YARP 路由无需改动（`/api/bff/**` 已配置）
- **前端**：`frontend/src/stores/auth.ts`、`frontend/src/api/clients.ts`、App 启动序列、登录/注销页
- **依赖**：BFF 新增 Redis（已有 Docker Compose 实例）用于分布式刷新锁兜底
- **安全**：Access Token 生命周期降为内存级（页面关闭即清除），Refresh Token 对 JS 完全不可见
