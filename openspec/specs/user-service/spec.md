## Purpose

JWT issuance, refresh, blacklist, and protected routes for the account and auth API.
## Requirements
### Requirement: JWT issuance surface

The user service API SHALL expose token issuance endpoints for login, refresh flows, and successful email verification completion. Issued access tokens MUST include `sub` as the user primary identifier. If `userId` is included, it MUST be exactly equal to `sub`. Login SHALL issue tokens only to accounts whose email address has been verified according to `email-verification-registration`. After a verification artifact is validated and the account is marked verified, the service SHALL be able to issue access and refresh tokens for normal protected API access with the same claims and rotation rules as the login success path, without requiring a separate password login. Registration-related issuance SHALL conform to the same capability and SHALL NOT grant access to protected business APIs until verification is complete unless an explicitly scoped token type is documented in tasks.

#### Scenario: Login issues consistent identity claims

- **WHEN** a client completes login with valid credentials for a verified account
- **THEN** the response SHALL include a signed access token containing `sub`
- **AND** if `userId` claim is present, it SHALL be exactly equal to `sub`

#### Scenario: Refresh issues consistent identity claims

- **WHEN** a client refreshes token with a valid refresh token session
- **THEN** the newly issued access token SHALL preserve the same user identity key in `sub`
- **AND** if `userId` claim is present, it SHALL be exactly equal to `sub`

#### Scenario: Unverified account cannot obtain tokens via login

- **WHEN** a client attempts login for an account that has not completed email verification
- **THEN** the service SHALL NOT issue access and refresh tokens for normal protected API access as defined in `email-verification-registration`

#### Scenario: Successful email verification issues session tokens

- **WHEN** the user service completes email verification for an account and is ready to issue tokens per `email-verification-registration`
- **THEN** the response SHALL include access and refresh tokens (or equivalent documented session credentials) that match login issuance semantics for verified accounts
- **AND** the client SHALL NOT need a subsequent password login solely to obtain those tokens

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

