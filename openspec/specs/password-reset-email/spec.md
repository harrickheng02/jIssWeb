# password-reset-email Specification

## Purpose
TBD - created by archiving change password-reset-email-flow. Update Purpose after archive.
## Requirements
### Requirement: 忘记密码请求不枚举已注册邮箱

系统 SHALL 接受按邮箱发起的密码重置请求，且 SHALL 不在客户端可见响应中暴露该邮箱是否已注册。无论是否存在匹配的已验证账号，响应 SHALL 使用相同的成功语义。

#### Scenario: 未注册邮箱的请求

- **WHEN** 客户端为未关联已验证注册账号的邮箱提交密码重置请求
- **THEN** 响应 SHALL 与「将触发重置邮件」时使用的成功形态一致
- **AND** 客户端 SHALL 不得收到表明账号不存在的机器可读提示

#### Scenario: 触发限流

- **WHEN** 按客户端身份或邮箱键计数的密码重置请求超过配置上限
- **THEN** 服务 SHALL 拒绝后续请求，并返回与其他敏感认证端点一致的限流结果

### Requirement: 密码重置凭据一次性且有时效

系统 SHALL 在配置的最大时间内使密码重置凭据过期，且 SHALL 在密码修改成功后立即作废该凭据，使其不可重复使用。

#### Scenario: 成功重置后凭据作废

- **WHEN** 客户端使用有效的重置凭据完成密码修改
- **THEN** 该凭据 SHALL 在响应完成前即被作废

#### Scenario: 重复使用已消费凭据失败

- **WHEN** 客户端在凭据已被使用后仍用同一凭据提交新密码
- **THEN** 服务 SHALL 拒绝请求，并返回可与网络错误区分的稳定错误结果

### Requirement: 无效或过期重置凭据有明确结果

系统 SHALL 对无效、过期或已使用的重置凭据返回稳定、机器可读的错误码或等价结果，以便客户端展示明确文案。

#### Scenario: 凭据已过期

- **WHEN** 客户端提交已过期的重置凭据
- **THEN** 服务 SHALL 拒绝操作且 SHALL 不修改密码

### Requirement: 成功重置后建立新会话并轮换既有 refresh 会话

在账号密码重置成功后，系统 SHALL 按与已验证账号成功密码登录相同的语义签发新的 access 与 refresh 令牌，且 SHALL 按服务策略使该用户既有 refresh 会话失效，从而使此前签发的 refresh 无法再换取新的 access。

#### Scenario: 重置后会话与登录一致

- **WHEN** 密码重置成功完成
- **THEN** 响应 SHALL 包含与该账号登录成功路径一致的 access 与 refresh（或文档化的等价会话凭据）

#### Scenario: 旧 refresh 不再可用

- **WHEN** 客户端持有该账号在成功密码重置之前签发的 refresh 令牌
- **THEN** 使用该旧令牌进行 refresh SHALL 失败，并返回认证失败类结果

