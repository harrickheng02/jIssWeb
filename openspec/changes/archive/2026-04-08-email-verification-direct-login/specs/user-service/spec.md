## MODIFIED Requirements

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
