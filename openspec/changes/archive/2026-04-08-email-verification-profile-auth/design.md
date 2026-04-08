## Context

JIssWeb 已具备 User.Api（注册/登录/刷新/吊销）、Customer.Api（`CustomerRecord` CRUD）、前端 Vite 代理与 Pinia 存 token。注册当前即签发令牌且邮箱未校验；客档模型未区分「个人 Profile」与「多条 Customer」。本设计落实 proposal 中的邮箱验证流、验签、限流、Profile 边界与「记住我」第一期行为。

## Goals / Non-Goals

**Goals:**

- 邮箱为唯一登录标识；完成注册以**邮件链接验证成功**为界，成功后进入专用成功页再跳转登录。
- 验证链接由**服务端生成并验签**（不依赖前端篡改）；一次性、短 TTL；兑换逻辑在 User 服务完成。
- 未验证账号：**仅允许**重发验证邮件、静态说明等白名单能力；**不**对业务 API（含 Customer/Profile）发放有效会话；对 `register` / `resend` **按邮箱与 IP（可选）限流**，Redis 计数键带 TTL，避免无界增长。
- **Profile**：每用户至多一条，与 **Customer** 多条记录分模型、分路由；Profile 承载昵称、生日、性别及后续论坛/博客/推荐扩展字段（字段可分期落地）。
- 登录页「免登录 / 记住我」：第一期 **持久化 refresh token**（如 `localStorage`）并在应用启动时 **静默 refresh**；失败则清除本地凭证。

**Non-Goals:**

- 设备指纹、异地登录告警、强制下线、MFA（另开 change）。
- 邮件模板品牌化、多语言、高可用发信架构的完整 SLA（仅约定可插拔提供方与配置项）。
- Profile 与 Customer 的复杂关联规则（如「必须先有 Profile」）——默认不强制，除非后续产品规定。

## Decisions

| 决策 | 选择 | 理由 |
|------|------|------|
| 验证前是否发 token | **不发**可用于资源访问的 access/refresh（或仅发**极短 TTL、仅用于「完成验证」相关端点**的受限令牌，实现择一；推荐 **验证完成前不发 refresh**） | 与「仅重发验证页」一致，减少未验证态攻击面 |
| 验证链接形态 | **服务端验签** payload（HMAC-SHA256 或对称签名的紧凑结构），含 `sub` 或邮箱、`exp`、`purpose=verify`；**一次性**：兑换后写入 Redis/Mongo 防重放至 TTL | 安全要求；避免仅靠随机串无绑定 |
| 邮件链接落地 | **优先**：链接指向 **后端 GET/POST `/api/auth/verify-email`**，验签成功后 **302** 到前端 `/register/verified`（无敏感信息）或带**短期** `exchange` code 由前端再换会话（择一实现） | 验签必须在服务端 |
| 限流 | **User 服务**对 `register`、`resend-verification`：**每邮箱**、**每 IP**（若可取）滑动窗口或固定窗口 + Redis；超限返回 429 与统一 `ApiResult` | 防刷库、防塞 Redis |
| Profile 存放 | **Customer 服务**（与现有客档同进程/同库不同集合），路由如 `api/profile` 与 `api/customers` 分离 | 与 proposal 一致；复用 JWT 校验与 `sub` |
| 记住我 | 勾选后 **持久化 refresh**；未勾选用 **sessionStorage** 或内存级仅会话期 refresh | 第一期简单可测 |

**备选（未采纳）**：验证邮件仅含前端路由，由前端把 token POST 给后端——验签仍在后端可行，但链接暴露面更大，故优先后端首跳验签再重定向。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| SMTP 不可用导致注册不可用 | 开发环境可用日志/文件后门；配置健康检查与告警 |
| 限流过严误伤共享 IP | 以邮箱维度为主，IP 为辅；可调配额 |
| 静默 refresh 泄露 refresh 存储 | 第一期接受 localStorage 风险；后续 change 迁 httpOnly cookie |
| Profile 与 User 邮箱变更 | 首期 Profile 不存邮箱；邮箱仅在 User；变更邮箱另 spec |

## Migration Plan

1. 为 `users` 增加 `EmailVerifiedAt`（或等价）与必要索引；历史用户可 **一次性脚本** 标为已验证或要求重新验证（产品定）。
2. 部署 User → 再部署 Customer（Profile）→ 前端；开关可用配置控制「强制验证」。
3. 回滚：关闭强制验证标志（若实现）、恢复上一版本镜像；注意已发验证链接 TTL 自然过期。

## Open Questions

- 历史未验证用户数据迁移策略（默认标已验证 vs 强制重验）。
- 验证邮件中前端 `baseUrl` 多环境配置方式。
- Profile 是否在验证成功后由**异步任务**创建默认空文档，还是首次 `GET /api/profile` 时 upsert。
