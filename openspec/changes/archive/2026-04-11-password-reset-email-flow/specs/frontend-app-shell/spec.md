## ADDED Requirements

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
