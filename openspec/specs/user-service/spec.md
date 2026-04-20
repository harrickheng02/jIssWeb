## Purpose

JWT issuance, refresh, blacklist, and protected routes for the account and auth API.
## Requirements
### Requirement: JWT issuance surface

The user service API SHALL expose token issuance endpoints for login, refresh flows, and successful email verification completion. Issued access tokens MUST include `sub` as the user primary identifier. If `userId` is included, it MUST be exactly equal to `sub`. Each issued access token MUST include a `forumRole` claim whose string value is exactly one of `member`, `moderator`, or `admin`, representing the caller's global forum role as defined in `token-identity-consistency`. Login SHALL issue tokens only to accounts whose email address has been verified according to `email-verification-registration`. After a verification artifact is validated and the account is marked verified, the service SHALL be able to issue access and refresh tokens for normal protected API access with the same claims and rotation rules as the login success path, without requiring a separate password login. Registration-related issuance SHALL conform to the same capability and SHALL NOT grant access to protected business APIs until verification is complete unless an explicitly scoped token type is documented in tasks.

#### Scenario: Login issues consistent identity claims

- **WHEN** a client completes login with valid credentials for a verified account
- **THEN** the response SHALL include a signed access token containing `sub`
- **AND** if `userId` claim is present, it SHALL be exactly equal to `sub`
- **AND** the access token SHALL include `forumRole` with value `member`, `moderator`, or `admin`

#### Scenario: Refresh issues consistent identity claims

- **WHEN** a client refreshes token with a valid refresh token session
- **THEN** the newly issued access token SHALL preserve the same user identity key in `sub`
- **AND** if `userId` claim is present, it SHALL be exactly equal to `sub`
- **AND** the access token SHALL include `forumRole` reflecting the user account's forum role **at issuance time**

#### Scenario: Unverified account cannot obtain tokens via login

- **WHEN** a client attempts login for an account that has not completed email verification
- **THEN** the service SHALL NOT issue access and refresh tokens for normal protected API access as defined in `email-verification-registration`

#### Scenario: Successful email verification issues session tokens

- **WHEN** the user service completes email verification for an account and is ready to issue tokens per `email-verification-registration`
- **THEN** the response SHALL include access and refresh tokens (or equivalent documented session credentials) that match login issuance semantics for verified accounts
- **AND** the client SHALL NOT need a subsequent password login solely to obtain those tokens
- **AND** issued access tokens SHALL include `forumRole` as for the login success path

### Requirement: Refresh token and blacklist support
The user service SHALL provide refresh token issuance and rotation, and SHALL enforce Redis-backed blacklist or revocation checks before issuing new access tokens.

#### Scenario: Revoked refresh token
- **WHEN** a refresh token has been revoked or blacklisted
- **THEN** refresh SHALL fail and the service SHALL return HTTP 401

#### Scenario: Successful refresh rotation
- **WHEN** a refresh request is accepted
- **THEN** the previous refresh token session SHALL be invalidated according to service policy and a new refresh token SHALL be issued

### Requirement: JWT validation on protected routes

The user service SHALL configure JWT bearer authentication such that protected routes reject missing or invalid tokens with HTTP 401.

#### Scenario: Missing bearer token

- **WHEN** a client calls a protected endpoint without an `Authorization: Bearer` header
- **THEN** the response status SHALL be 401

### Requirement: Service identity and routing

The user service SHALL use route prefix `api` and SHALL be deployable as a standalone process with its own documented HTTP port separate from other domain services.

#### Scenario: Health endpoint

- **WHEN** a client sends `GET` to the user service health URL
- **THEN** the response SHALL be HTTP 200 with the unified envelope indicating success

### Requirement: Stable authentication failure feedback

The user service SHALL return stable error codes or equivalent machine-readable outcomes for common authentication failures so the frontend can distinguish wrong credentials, missing account, throttling, verification-required states, or captcha escalation paths.

#### Scenario: Wrong password distinguished from throttling
- **WHEN** login fails because credentials are wrong and the caller is not rate-limited
- **THEN** the service SHALL return an authentication failure response that is distinguishable from rate limit or captcha-required responses

### Requirement: Login abuse protection integration

The user service SHALL support login failure counting, throttling, or equivalent brute-force mitigation behavior, and SHALL expose a response shape that allows the frontend to escalate to captcha or temporary blocking UX when policy requires it.

#### Scenario: Repeated failures trigger stronger response
- **WHEN** a client exceeds the configured login failure threshold for an identity or IP window
- **THEN** the service SHALL return a response that indicates throttling, temporary blocking, or captcha-required handling according to service policy

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
- **AND** 签发的 access token SHALL 包含 `forumRole`，语义与登录成功路径一致

### Requirement: 密码重置失败的稳定结果

用户服务 SHALL 为密码重置流程返回稳定错误码或机器可读结果，覆盖无效凭据、过期凭据、重复使用凭据及密码策略违反，并在可能情况下与通用服务器错误区分。

#### Scenario: 无效凭据

- **WHEN** 客户端使用无法识别或格式错误的重置凭据提交密码修改
- **THEN** 服务 SHALL 拒绝请求且 SHALL 不更新密码

