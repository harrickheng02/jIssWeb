## MODIFIED Requirements

### Requirement: JWT issuance surface

The user service API SHALL expose token issuance endpoints for login and refresh flows. Issued access tokens MUST include `sub` as the user primary identifier. If `userId` is included, it MUST be exactly equal to `sub`.

#### Scenario: Login issues consistent identity claims

- **WHEN** a client completes login with valid credentials
- **THEN** the response SHALL include a signed access token containing `sub`
- **AND** if `userId` claim is present, it SHALL be exactly equal to `sub`

#### Scenario: Refresh issues consistent identity claims

- **WHEN** a client refreshes token with a valid refresh token session
- **THEN** the newly issued access token SHALL preserve the same user identity key in `sub`
- **AND** if `userId` claim is present, it SHALL be exactly equal to `sub`

## ADDED Requirements

### Requirement: Refresh token and blacklist support
The user service SHALL provide refresh token issuance and rotation, and SHALL enforce Redis-backed blacklist or revocation checks before issuing new access tokens.

#### Scenario: Revoked refresh token
- **WHEN** a refresh token has been revoked or blacklisted
- **THEN** refresh SHALL fail and the service SHALL return HTTP 401

#### Scenario: Successful refresh rotation
- **WHEN** a refresh request is accepted
- **THEN** the previous refresh token session SHALL be invalidated according to service policy and a new refresh token SHALL be issued
