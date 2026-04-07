## ADDED Requirements

### Requirement: JWT issuance surface

The user service API SHALL expose at least one HTTP endpoint that returns a signed JWT for a placeholder identity when invoked in development, using the same `Jwt` configuration section (issuer, audience, signing key) as documented in the design.

#### Scenario: Obtain token for downstream calls

- **WHEN** a client requests a token from the issuance endpoint with valid skeleton parameters
- **THEN** the response SHALL include a non-empty JWT string that validates against the configured issuer and audience

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
