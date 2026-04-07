## ADDED Requirements

### Requirement: Subject-first identity claim contract
JWT payload MUST use `sub` as the primary and authoritative user identifier across services.

#### Scenario: Resource service resolves identity
- **WHEN** a resource service receives a valid bearer token
- **THEN** it MUST resolve user identity from `sub` as the primary key

### Requirement: userId semantic alias consistency
If `userId` claim is present, it MUST be exactly equal to `sub`.

#### Scenario: Token includes both claims
- **WHEN** a token contains both `sub` and `userId`
- **THEN** `userId` MUST equal `sub` with exact string equality

#### Scenario: Token includes only sub
- **WHEN** a token contains `sub` and omits `userId`
- **THEN** the token MUST still be considered structurally valid for identity extraction

### Requirement: Mismatch handling
Tokens with missing `sub`, or with `userId` present but not equal to `sub`, MUST be rejected as invalid identity tokens.

#### Scenario: Claim mismatch
- **WHEN** a token has `sub` and `userId` with different values
- **THEN** the service MUST reject the request with HTTP 401
