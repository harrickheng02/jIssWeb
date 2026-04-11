## 为什么

论坛账号已支持邮箱验证与登录，但「忘记密码」仍为前端占位，用户无法自助恢复访问；需在 user-service 与 SPA 落地邮箱重置闭环，并与既有 JWT、限流、错误码体系一致。

## 变更内容

- user-service：提供「请求重置（按邮箱）」「校验令牌并设新密码」等端点；重置邮件中的令牌短 TTL、一次性使用；重置成功后签发与登录一致的 access/refresh，并吊销该用户既有 refresh 会话以防旧凭据残留。
- 对外行为：不暴露邮箱是否注册（统一成功文案 + 限流）；非法/过期令牌有明确、稳定错误码。
- 前端：`/auth/forgot` 提交邮箱；邮件链接落地后的重置页提交新密码；成功后写入会话并进入已登录壳层（与现有 auth store 一致），移除「敬请期待」占位。
- 配置：与邮件发送、链接基址、TTL、限流相关的环境项（与 EmailVerification 模式可并列、密钥分离）。

## 能力

### 新增能力

- `password-reset-email`：邮箱发起重置、防用户枚举、重置令牌规则（单次、短 TTL）、成功后自动登录与会话轮换；明确不包含短信/MFA。

### 修改能力

- `user-service`：增加密码重置相关 SHALL（端点语义、错误码、与 refresh 吊销策略）。
- `frontend-app-shell`：忘记密码与重置密码路由及成功后会话集成（替换仅占位）。

## 影响

- `backend` 下 user-service 项目：持久化或 Redis 存重置令牌、邮件发送、与现有 JWT/refresh 黑名单或轮换逻辑衔接。
- `frontend`：`ForgotPasswordView`、新增或扩展重置页、`stores/auth`、axios/API 封装。
- `docker-compose` / 部署：新增或扩展与密码重置相关的配置节。
- 与 `email-verification-registration`、`auth-page-experience` 并存；不改动「仅已验证邮箱可登录」的既有规则。
