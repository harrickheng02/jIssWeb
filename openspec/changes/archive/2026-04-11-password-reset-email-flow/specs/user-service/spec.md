## ADDED Requirements

### Requirement: 密码重置请求端点

用户服务 SHALL 提供 HTTP 端点，接受规范化邮箱地址以发起已验证账号的密码重置。该端点 SHALL 施加限流，且 SHALL 返回单一的、客户端可见的成功形态，不暴露邮箱是否已注册，并与 `password-reset-email` 一致。

#### Scenario: 触发限流

- **WHEN** 调用方超过配置的密码重置请求限制
- **THEN** 服务 SHALL 返回限流结果，前端可映射为用户可见提示，且 SHALL 不暗示账号是否存在

### Requirement: 密码重置完成端点

用户服务 SHALL 提供 HTTP 端点，接受有效的密码重置凭据与新密码，按 `password-reset-email` 校验凭据，更新存储的密码，按成功登录语义签发 access 与 refresh 令牌，并使用户此前的 refresh 会话失效。

#### Scenario: 重置完成并下发新会话

- **WHEN** 客户端提交有效重置凭据且新密码符合策略
- **THEN** 响应 SHALL 包含与已验证账号登录成功后相同的 access 与 refresh（或文档化的等价会话凭据）
- **AND** 该用户此前的 refresh 令牌 SHALL 不再能通过 refresh 成功

### Requirement: 密码重置失败的稳定结果

用户服务 SHALL 为密码重置流程返回稳定错误码或机器可读结果，覆盖无效凭据、过期凭据、重复使用凭据及密码策略违反，并在可能情况下与通用服务器错误区分。

#### Scenario: 无效凭据

- **WHEN** 客户端使用无法识别或格式错误的重置凭据提交密码修改
- **THEN** 服务 SHALL 拒绝请求且 SHALL 不更新密码
