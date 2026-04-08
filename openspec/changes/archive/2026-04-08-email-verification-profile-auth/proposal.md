## Why

当前注册即签发完整令牌、邮箱未校验，与「邮箱作为唯一登录手段、验证通过才算完成注册」不符；客档仅有 `CustomerRecord`，缺少与「个人资料」分离的 Profile 模型，不利于后续论坛、博客与推荐。本变更将邮箱验证流、后端验签、限流与 Profile/Customer 分模纳入规格，便于分阶段实现。

## What Changes

- 注册改为：**提交邮箱密码 → 创建待验证账号 → 发验证邮件**；验证成功前**不签发**可用于业务 API 的访问/刷新令牌（或仅签发受限范围，实现以 design 为准）。
- 未验证用户：**仅允许**访问「重发验证邮件」及静态说明等白名单；**注册/重发接口全局限流**，Redis/Mongo 键有界 TTL，避免滥用撑爆存储。
- 验证链接：**服务端生成并校验签名**（HMAC/JWT 等），绑定邮箱或用户 id、过期时间、一次性消费；验证成功后跳转前端成功页再进登录页。
- **Profile**：每用户**一条**（`ownerUserId`/`sub` 唯一），承载昵称、生日、性别及后续论坛/博客/推荐扩展字段；与 **Customer**（多条客档）**分集合或分资源**，互不替代。
- 登录页增加「免登录 / 记住我」：**第一期**用客户端保存 refresh token 并在应用启动时静默 refresh；设备绑定、风控等**另开 change**。
- 前端：验证成功路由、登录前拦截、控制台/消息 UX 与既有 shell 对齐。

## Capabilities

### New Capabilities

- `email-verification-registration`: 邮箱验证注册与重发、后端验签兑换、未验证期 API 边界与限流策略。
- `user-profile-record`: Profile 单例资源（相对 User 的 `sub`）、与 Customer 多条记录的边界与 API 形状。

### Modified Capabilities

- `user-service`: 注册/登录/令牌行为与邮箱验证状态挂钩；新增验证兑换、重发邮件等端点及邮件相关配置说明。
- `customer-profile-service`: 在现有客档能力上增加 Profile 域路由与约束；明确不签发令牌。
- `customer-record-crud`: 明确 Customer 为多条业务记录，与 Profile 文档分离。
- `frontend-app-shell`: 验证与登录相关路由、未验证态导航、「记住我」与静默刷新行为。

## Impact

- **后端**：`JIssWeb.User.Api`（账号状态、发信、验签、限流）；`JIssWeb.Customer.Api`（Profile 模型与 API，Customer 与 Profile 分离）；可能新增邮件提供方配置与密钥。
- **前端**：`HomeView`/路由、`auth` store、axios 拦截与静默 refresh。
- **基础设施**：SMTP 或云邮件、用于签名的服务端密钥；Redis 用于限流与短期一次性 token（有界）。
