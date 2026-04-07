## ADDED Requirements

### Requirement: Domain API host

The customer profile (客档) service SHALL host an ASP.NET Core Web API project that starts independently and exposes OpenAPI (Swagger) in Development.

#### Scenario: Swagger availability

- **WHEN** the service runs with `ASPNETCORE_ENVIRONMENT=Development`
- **THEN** Swagger UI SHALL be reachable at the configured Swagger path

### Requirement: JWT validation only

The customer profile service SHALL validate JWTs issued by the user service using the shared signing key and issuer or audience settings; it SHALL NOT expose token issuance endpoints as part of this skeleton.

#### Scenario: Valid token accepted

- **WHEN** a client calls a protected placeholder endpoint with a valid Bearer token from the user service
- **THEN** the response SHALL not be 401 solely due to token validation

### Requirement: Health and namespace

The service SHALL expose `GET /api/health` (or equivalent agreed path) using the unified response envelope and SHALL use controller or route naming that clearly identifies the customer profile domain.

#### Scenario: Anonymous health

- **WHEN** a client calls the health endpoint without authentication if health is anonymous
- **THEN** the response SHALL return success in the unified envelope
