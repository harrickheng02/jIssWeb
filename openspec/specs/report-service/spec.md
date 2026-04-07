## ADDED Requirements

### Requirement: Read-oriented API shell

The report (报表) service SHALL provide a runnable Web API intended for read-only or aggregation-style endpoints; skeleton controllers SHALL not perform destructive writes except for operational health or configuration probes explicitly documented.

#### Scenario: No hidden write endpoints in skeleton

- **WHEN** the skeleton is delivered
- **THEN** default scaffolded controllers for reporting SHALL use `GET` for sample operations or document any non-GET as explicitly out-of-band for later work

### Requirement: JWT validation

The report service SHALL validate JWTs from the user service using the shared configuration.

#### Scenario: Unauthorized access blocked

- **WHEN** a client calls a protected report placeholder without a Bearer token
- **THEN** the response SHALL be 401

### Requirement: Health endpoint

The report service SHALL expose a health endpoint with the unified response envelope.

#### Scenario: Health check

- **WHEN** a client calls the health endpoint
- **THEN** the response SHALL include `success: true` in the envelope for normal operation
