## ADDED Requirements

### Requirement: Unified API response envelope

The system SHALL serialize successful and error API payloads using a single JSON envelope with at least `success`, optional `data`, optional `message`, and optional `code` fields, consistent across all backend services in this change.

#### Scenario: Successful JSON response shape

- **WHEN** a client calls any health or placeholder endpoint that succeeds
- **THEN** the response body SHALL include `success: true` and any payload under `data` as defined by that endpoint

#### Scenario: Error JSON response shape

- **WHEN** an unhandled failure is processed by global exception handling
- **THEN** the response body SHALL include `success: false` and a `message` suitable for clients, and MAY include a machine-oriented `code`

### Requirement: Global exception handling registration

Each ASP.NET Core API project in this change SHALL register the shared exception handling middleware (or equivalent) so that uncaught exceptions do not return raw stack traces to clients in production configuration.

#### Scenario: Middleware order

- **WHEN** the HTTP pipeline is built for any service API
- **THEN** exception handling SHALL be registered before authorization-protected endpoints are executed according to the shared design

### Requirement: Per-service configuration sections

Each service SHALL read MongoDB and Redis settings from configuration sections that are namespaced or otherwise distinct per service so that connection strings and database names cannot collide when running locally with one shared `appsettings` pattern.

#### Scenario: Distinct Redis usage

- **WHEN** two services run concurrently on one developer machine
- **THEN** their configuration SHALL allow different Redis logical databases or key prefixes without key collisions for skeleton placeholders

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
