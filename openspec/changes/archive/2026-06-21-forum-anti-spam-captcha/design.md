## Context

当前注册链路（`AuthController.Register`）已有邮箱格式校验、密码强度校验、Redis 频率限流（per-email + per-IP），但缺少"发起方是否为人类"的身份层验证。分布式 botnet 可以绕过单 IP 限流低速渗透，批量创建待验证账号消耗邮件配额。

change-B（#26）已完成 Redis 分布式限流，是本 change 的依赖前提。#30（AI 智能体账号协议）定义了 `accountType: agent` JWT claim，但尚未实现；本 change 预留豁免桩，#30 落地后无需修改本文件。

## Goals / Non-Goals

**Goals:**

- 注册请求时通过 Cloudflare Turnstile 验证发起方为人类
- `CaptchaSettings.Enabled = false` 时完全跳过验证（本地开发 / CI 无网络环境）
- 预留 `accountType: agent` 豁免分支（当前为死代码，#30 后自动激活）
- mock-able `ICaptchaVerifier` 接口，集成测试无需真实 CF 网络调用

**Non-Goals:**

- 登录、找回密码等其他端点的 CAPTCHA
- 首次发帖 CAPTCHA
- 设备指纹、短信验证
- agent 账号创建逻辑（属于 #30）

## Decisions

### 决策 1：验证点选在注册请求而非邮件验证后

**选择**：在 `POST /api/auth/register` 最前端校验。

**理由**：在账号写入数据库之前拦截，避免大量 `EmailVerifiedAtUtc = null` 的僵尸账号积压，不需要引入"首次发帖"的状态机或额外数据字段。

**备选**：邮件验证后 + 首次发帖时校验——被排除，因为用户需经历两次 CAPTCHA，体验差；且"首次"语义模糊，需要额外 flag 持久化。

---

### 决策 2：服务抽象层 ICaptchaVerifier

```
ICaptchaVerifier
└── VerifyAsync(token: string, remoteIp: string?) : Task<bool>

实现：
  TurnstileCaptchaVerifier  ← 生产（调用 CF siteverify API）
  AlwaysPassCaptchaVerifier ← 测试用 mock
```

**理由**：与 `AuthController` 解耦，允许测试用 mock 替换，未来可无痛切换到其他 CAPTCHA 提供商（hCaptcha 等）。注册为 `IHttpClientFactory` 消费的命名客户端，避免手动管理 `HttpClient` 生命周期。

---

### 决策 3：agent 豁免以 JWT claim 判断，置于 CAPTCHA 校验之前

```csharp
// AuthController.Register() 校验顺序：
// 1. 邮箱 / 密码基础校验
// 2. 频率限流（已有）
// 3. CAPTCHA 检查：
//    a. Enabled == false → 跳过（开发旁路）
//    b. accountType claim == "agent" → 跳过（#30 桩，当前永不触发）
//    c. captchaToken 为空 → 400 CAPTCHA_REQUIRED
//    d. 验证失败 → 400 CAPTCHA_INVALID
// 4. 业务逻辑（已有）
```

**为何不用 Middleware / ActionFilter**：注册是唯一需要 CAPTCHA 的端点，提取为通用 Filter 过度设计；且注册是 `[AllowAnonymous]`，Filter 中读取 JWT claim 需要额外处理空 Principal。

**为何 agent 豁免不需要 JWT**：agent 账号走内部 API 创建，注册时根本不会调用 `POST /register`——豁免桩是为了防御性编程（万一调度系统误调），不是主流程。

---

### 决策 4：前端 widget 使用 vue-turnstile

**理由**：零依赖的 Vue 3 Composition API 封装，包体积极小（< 2 kB），维护活跃。备选原生 script 注入需手动处理 widget 生命周期（reset on error, cleanup on unmount），成本更高。注：`@marsidev/vue-turnstile` 在 npm 上仅有 React 版本，实际安装包为 `vue-turnstile` by ruigomes。

`siteKey` 从 `import.meta.env.VITE_TURNSTILE_SITE_KEY` 读取；`captchaToken` 存储在 `registerForm` 响应式对象中，与现有表单模式一致。

---

### 决策 5：Turnstile 验证失败策略 — fail-closed

注册不是高频操作，CF siteverify API 可用性极高（SLA 99.99%），fail-closed（验证失败/超时 → 拒绝注册）是正确的默认策略。如果未来 CF 出现区域性故障，可通过 `CaptchaSettings.Enabled = false` 热配置快速旁路（需要重启服务，可接受）。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| Cloudflare Turnstile 在部分地区延迟高 | Turnstile 无感验证通常在页面加载时并行完成，用户感知为零；极端情况可降级为有感挑战 |
| CF siteverify API 超时导致注册阻塞 | `HttpClient` 配置 5s 超时；超时记录 Warning 日志并返回 `CAPTCHA_INVALID`（fail-closed） |
| 开发环境缺乏 CF 网络访问 | `CaptchaSettings.Enabled = false` 完全跳过；Turnstile 官方测试 siteKey 也可用于 staging |
| agent 豁免桩长期无人激活变为永久死代码 | #30 的验收标准明确要求联动验证；pm-plan 依赖链记录了此关系 |
| `VITE_TURNSTILE_SITE_KEY` 泄露 | siteKey 是公开值，设计上可暴露在前端；secretKey 仅存 `appsettings.Local.json`，不进 git |
