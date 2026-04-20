## MODIFIED Requirements

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

### Requirement: 密码重置完成端点

用户服务 SHALL 提供 HTTP 端点，接受有效的密码重置凭据与新密码，按 `password-reset-email` 校验凭据，更新存储的密码，按成功登录语义签发 access 与 refresh 令牌，并使用户此前的 refresh 会话失效。

#### Scenario: 重置完成并下发新会话

- **WHEN** 客户端提交有效重置凭据且新密码符合策略
- **THEN** 响应 SHALL 包含与已验证账号登录成功后相同的 access 与 refresh（或文档化的等价会话凭据）
- **AND** 该用户此前的 refresh 令牌 SHALL 不再能通过 refresh 成功
- **AND** 签发的 access token SHALL 包含 `forumRole`，语义与登录成功路径一致
