## ADDED Requirements

### Requirement: Cross-service identity claim parsing rule
All backend services MUST apply a consistent JWT identity parsing rule: use `sub` as the authoritative user identifier, and treat `userId` as optional semantic alias only.

#### Scenario: Service parses authenticated principal
- **WHEN** a service extracts identity from authenticated JWT claims
- **THEN** it MUST derive the user primary identifier from `sub`

### Requirement: Claim consistency failure semantics
If `userId` exists and does not equal `sub`, services MUST treat the token as invalid and return unauthorized.

#### Scenario: Inconsistent identity claims
- **WHEN** a request carries a JWT where `sub` and `userId` differ
- **THEN** the service MUST stop request processing as unauthorized and return HTTP 401
