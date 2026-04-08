## Purpose

Host the customer profile (客档) API: JWT-protected CRUD and profile routes backed by shared user identity (`sub`).

## Requirements
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

### Requirement: Protected customer-domain business endpoints

The customer profile service SHALL expose at least one business route group under the `api` prefix (for example `api/customers`) for customer record CRUD, in addition to health and any skeleton placeholders; these endpoints SHALL require JWT bearer authentication and SHALL enforce per-owner data access using `sub` as defined in `customer-record-crud`.

#### Scenario: Unauthenticated CRUD request rejected

- **WHEN** a client calls a customer CRUD endpoint without a Bearer token
- **THEN** the response SHALL be HTTP 401

### Requirement: No token issuance in customer service

The customer profile service SHALL NOT add token issuance endpoints; authentication continues to originate from the user service.

#### Scenario: No login endpoint on customer service

- **WHEN** the customer service is deployed
- **THEN** it SHALL NOT expose `/api/auth/login` or equivalent issuance routes as part of this change

### Requirement: Profile business routes

The customer profile service SHALL expose authenticated profile routes distinct from `api/customers` (for example under `api/profile`) for reading and updating the single profile per user as defined in `user-profile-record`.

#### Scenario: Profile requires authentication

- **WHEN** a client calls profile endpoints without a Bearer token
- **THEN** the response SHALL be HTTP 401

### Requirement: Profile ownership enforced

Profile read and write operations SHALL use `sub` from the validated JWT as the owner key; the service SHALL NOT return or mutate another user's profile.

#### Scenario: Cross-user profile access denied

- **WHEN** a client attempts to access a profile identifier that does not belong to the caller's `sub`
- **THEN** the service SHALL return HTTP 404 or 403 per tasks and SHALL NOT leak existence of other users' profiles

