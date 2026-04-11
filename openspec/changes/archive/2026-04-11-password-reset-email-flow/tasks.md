## 1. User service：数据与配置

- [x] 1.1 新增 `PasswordReset` 配置（密钥、链接基址、TTL、按 IP/按邮箱限流），与 `EmailVerification` 分离；接入 appsettings 与 docker-compose 示例
- [x] 1.2 按设计在 Redis 或数据库持久化重置凭据（opaque id + 哈希、过期、单次使用）；按需索引或键 TTL
- [x] 1.3 按选定链接形态（后端跳转+exchange 或直接 token）发送重置邮件；若已有邮件抽象则复用

## 2. User service：API

- [x] 2.1 实现按邮箱请求重置端点（如 `POST`）：统一成功响应体、限流、仅当账号存在时发信与审计
- [x] 2.2 实现完成重置端点：校验凭据、密码策略、更新密码、按登录语义签发 access/refresh、吊销该用户其余 refresh 会话
- [x] 2.3 对无效/过期/已使用凭据及密码策略失败返回稳定错误码；响应信封与现有认证 API 对齐
- [x] 2.4 若采用跳转+exchange：实现 GET 处理并重定向到 SPA `/auth/reset`，携带短期 exchange（按设计）

## 3. 前端

- [x] 3.1 将 `ForgotPasswordView` 从占位改为可提交邮箱、加载/错误态、通用成功提示
- [x] 3.2 新增重置路由与页面：新密码+确认，按后端约定从 query/hash 取凭据并提交
- [x] 3.3 成功后经现有 auth store 持久化令牌并进入应用（与登录一致）；处理 2.3 的错误码
- [x] 3.4 确保 Vite 代理（或统一入口）覆盖 user-service 新路径

## 4. 验证

- [x] 4.1 手工：申请 → 邮件 → 打开链接 → 重置 → 自动登录；再次使用同一链接应失败；重置后其他标签页旧 refresh 应失败
- [x] 4.2 确认未知邮箱仍呈现相同 UX，无枚举泄露
