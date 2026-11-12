## Context

当前 `JIssWeb.Frontend.Bff` 仅有一个 `BootstrapController`（健康检查），Gateway 的 YARP 路由 `/api/bff/**` 已正确指向 BFF（5095 端口），前端 `bffApi` 客户端已存在但实际未被任何业务路径使用。

前端 JWT 存放在 `localStorage`（`jissweb.jwt`）和 `localStorage`（`jissweb.refresh.local`），对 XSS 完全透明。首页初始化需要至少 5 个并发请求直打 Model API；用户状态（profile + 未读数）需分别请求 Customer API 和 Model API。

本次设计分三个功能层，依顺序落地：
1. **Cookie Session**（鉴权代理）
2. **Me Bundle**（用户状态聚合）
3. **Forum Init Bundle**（论坛首页聚合）

## Goals / Non-Goals

**Goals:**
- 将 Refresh Token 迁移至 HttpOnly Cookie，彻底阻断 JS 可访问的长效凭证
- Access Token 仅存活于 Pinia 内存（15 分钟），页面关闭即清除
- BFF 侧两个聚合端点将前端多次 RTT 压缩为单次
- 多标签页并发续期由 BroadcastChannel + BFF Redis 锁双层保护

**Non-Goals:**
- 不做全站 BFF 代理；mutation 类请求（like/reply/mod ops）继续由前端携带内存 Bearer 直打下游
- 不改变 User API 的 token 签发逻辑和有效期配置
- 不引入 Antiforgery Token 中间件
- 不为移动端/小程序设计独立路由；若未来引入多端，应拆分独立 BFF 实例而非在现有 BFF 中用条件分支区分端

## Decisions

### D1：Token Handler Pattern（而非全代理）

**选择**：BFF 承接鉴权（Cookie Session），聚合只做只读场景（forum-init、me），mutation 类保持现状。

**理由**：全代理需要在 BFF 中复制所有下游端点，维护成本极高且与"BFF 不替代 Gateway 路由"的架构原则冲突（见 `frontend-bff-service` spec）。Token Handler Pattern 以最小改动获得最高安全收益。

**备选**：全代理模式——否决，改动面过大，与当前团队规模不匹配。

---

### D2：Cookie 属性三件套

```
Set-Cookie: refresh_token=<value>;
  HttpOnly; SameSite=Strict; Secure; Path=/api/bff/auth; Max-Age=<对齐 User API 配置>
```

- `Path=/api/bff/auth`：Cookie 仅在 BFF 鉴权路径下自动附带，不污染其他请求
- `Secure`：生产 HTTPS 强制；开发环境通过 `ASPNETCORE_ENVIRONMENT=Development` 判断豁免
- `Max-Age`：读取 User API refresh token 过期时间配置（`appsettings.Local.json` → `Jwt:RefreshTokenExpiryDays`），不自行定义

---

### D3：CSRF 防护——Custom Header（方案 β）

**选择**：BFF 中间件检查 `X-BFF-Source: web` Header；结合 `SameSite=Strict` 构成双层防护。

**理由**：纯 SPA + JSON API 场景，跨站 form/img 无法设置自定义 Header；现代浏览器已禁用可注入 Header 的旧插件。Antiforgery Token 方案需要额外下发接口，引入往返和状态，得不偿失。

---

### D4：多标签页续期协调——BroadcastChannel + Redis 轻量锁

```
前端（主防）：
  标签页 A 发起 refresh → 通过 BroadcastChannel 广播新 token → 其他标签页更新 Pinia store

BFF（兜底）：
  接收 refresh 请求时，SET NX redis key "bff:refresh:<refresh_token_hash>" EX 10
  若 SET 失败（已有进行中的 refresh），等待 200ms 后重试读取新 access token
  避免同一 refresh token 被并发 rotate 两次导致 User API 报 token 已失效
```

Redis key 使用 refresh token 的 SHA-256 前缀（非明文），TTL 10 秒。

---

### D5：页面刷新时的静默登录恢复

```
main.ts（Pinia 初始化后，app.mount() 之前，fire-and-forget）：
  GET /api/bff/auth/token  (携带 Cookie，无 Bearer)
  → 成功：BFF 用 refresh cookie 向 User API 续期，返回新 access token → Pinia store
  → 401：Cookie 不存在或已过期 → clearAuth()，无跳转（router guard 在需要时再跳）
```

> **实现说明**：原设计写 `App.vue onMounted`，实际落地在 `main.ts` 的 Pinia 初始化之后、`app.mount()` 之前（fire-and-forget）。两者功能等价（均不阻塞渲染），但 `main.ts` 更早发起请求，减少 BFF 往返的等待窗口。

此端点标记 `[AllowAnonymous]`，仅依赖 Cookie 身份，返回 401 时同步在响应头清除 Cookie（`Max-Age=0`）。

**条件调用**：仅当 `sessionStorage` 存有 token（同 tab 刷新）或 `localStorage` 存有 `jissweb.session.hint`（跨 tab 关闭后重开）时才发起请求，避免未登录用户看到无意义的 401 噪声。

---

### D6：前端 auth.ts 改造

| 现状 | 目标 |
|------|------|
| `localStorage.setItem('jissweb.jwt', token)` | 移除，access token 改写 `sessionStorage`（见 D8） |
| `localStorage.setItem('jissweb.refresh.local', ...)` | 移除，refresh token 由 BFF Cookie 持有 |
| App 启动直接读 localStorage token | App 启动调用 `GET /api/bff/auth/token` 恢复（见 D5） |
| refresh 直调 `/api-user/auth/refresh` | refresh 改调 `/api/bff/auth/refresh`（BFF 读 Cookie） |

`clients.ts` 的 `runSingleFlightRefresh` 目标端点从 User API 改为 BFF；BroadcastChannel 逻辑在 `auth.ts` 中添加，不侵入 `createClient`。

---

### D8：Access Token 存活范围——sessionStorage（对 D6 的实现补充）

**原始设计**：access token 仅写 Pinia `ref<string | null>`（纯内存）。

**实际实现**：token 同时写入 `sessionStorage('jissweb.session.token')`。

**理由**：
- `sessionStorage` 生命周期与标签页绑定，关闭即清除，安全性与纯内存方案相当（不跨 tab 持久化）
- 同 tab 刷新（F5）后 token 立即可用，无需等待 `GET /api/bff/auth/token` 往返（~50-200ms），消除白屏窗口
- 多 tab 场景通过 BroadcastChannel 同步，新 tab 不依赖 sessionStorage 持久化

**附加标志**：`localStorage('jissweb.session.hint')` 为非敏感布尔标志（值为 `'1'`），仅用于判断"曾经登录过"，在登录时设置、登出时清除。BFF Cookie 真正持有凭证，hint 只影响是否发起 `/api/bff/auth/token` 请求，泄漏无安全影响。

---

### D7：BFF 编排边界——读侧聚合可扩展，写侧永不进入

**选择**：BFF 定位为"结构性瘦 BFF + 受控读侧编排扩展"。

| BFF 可以做（读侧聚合） | BFF 不能做（写侧 / 规则） |
|----------------------|--------------------------|
| 跨服务读聚合（forum-init、me、未来的 arena-state） | 指认合法性校验（配额、重复指认） |
| 游戏状态展示聚合（本轮倒计时、揭露结果汇总） | AIQI 计算逻辑 |
| Cookie Session 鉴权代理 | 揭露触发逻辑 |

**理由**：图灵场的游戏机制（静默指认、揭露周期、AIQI）天然跨多个服务，读侧聚合适合放 BFF；但写侧规则若进入 BFF 则形成领域逻辑泄漏，日后在 BFF 与领域服务之间产生重复维护负担。

**Agent 客户端（用户群 B）**完全绕过 BFF——AI 开发者的 agent 持 Bearer 直接访问 Gateway，不经过 Cookie Session 流程；BFF 的任何改动对 agent 透明，无需为 agent 在 BFF 开特殊路由。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| 静默恢复失败导致首次白屏时间增加 | `/api/bff/auth/token` 走 loopback，P99 预期 < 50ms；App loading 状态现已存在 |
| BFF Redis 锁在 Redis 不可用时失效 | 降级为无锁模式（仅 BroadcastChannel 保护）；User API refresh token rotate 失败时前端重新登录 |
| Cookie SameSite=Strict 在子域 OAuth 回调场景失效 | 当前无第三方 OAuth，若引入须升级为 SameSite=Lax + CSRF Token |
| 开发环境 `Secure` 限制需豁免 | 通过环境变量判断；本地走 HTTP loopback，不传输到外网，风险可接受 |
| BFF 故障导致无法登录 | BFF 是有状态服务（Redis 依赖），需加入健康检查监控；但 mutation 类请求不经 BFF，影响范围有限 |
| 下游 API 响应结构变更导致 BFF 静默返回错误数据 | BFF 集成测试用 `WireMock.Net` mock 固定版本的下游响应结构；下游 DTO 变更时 BFF 测试红灯先于运行时崩溃；聚合 controller 不允许出现业务条件判断，仅做字段映射 |
| BFF 逐渐积累领域业务逻辑破坏微服务单一职责 | Code review checklist 明确：BffController 只允许 HTTP 转发、`Task.WhenAll` 聚合、Cookie 读写；任何含业务规则的 PR（如"帖子状态为 X 时才返回 Y"）应被拒绝并下沉到对应领域服务 |

## Migration Plan

1. **后端先行**：在 BFF 实现完整 Cookie Session 端点，保持旧 `/api/bff/bootstrap` 不变
2. **前端灰度**：新增环境变量 `VITE_USE_BFF_AUTH=true`，默认 false；先在开发环境切换验证
3. **切换**：验证通过后移除旧 localStorage 路径，删除环境变量开关
4. **回滚**：若 Cookie Session 出现问题，可快速恢复 localStorage 路径（Git revert 前端 auth.ts）

## Open Questions

- User API 的 `Jwt:RefreshTokenExpiryDays` 当前值是多少？（影响 Cookie `Max-Age` 设置）
- BFF 访问 Customer API 和 Model API 时，是否需要将用户 Bearer token 转发？还是 BFF 使用服务间信任（如内部网络不验证）？→ 当前架构无服务间信任机制，BFF 需透传用户 Bearer。
