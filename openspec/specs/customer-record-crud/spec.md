## Purpose

Define customer record persistence and owner-scoped CRUD with unified API responses.

## Requirements
### Requirement: MongoDB collection for customer records

The customer service SHALL persist customer records in a dedicated MongoDB collection named and indexed in implementation tasks; each document SHALL include an `ownerUserId` field that stores the user identifier equal to the JWT `sub` claim.

#### Scenario: Record owner is set on create

- **WHEN** an authenticated user creates a customer record
- **THEN** the stored document SHALL have `ownerUserId` equal to that user's `sub` from the validated token

### Requirement: CRUD endpoints for authenticated users

The customer service SHALL expose HTTP endpoints to create, read (single and list scoped to owner), update, and delete customer records; all operations SHALL require a valid Bearer token and SHALL apply ownership using `sub` unless the endpoint is explicitly public (e.g., health).

#### Scenario: List returns only current user's records

- **WHEN** an authenticated user requests the list endpoint
- **THEN** the response SHALL include only customer records whose `ownerUserId` equals the caller's `sub`

#### Scenario: Cross-user access denied

- **WHEN** a user requests a record by id that exists but belongs to another `ownerUserId`
- **THEN** the service SHALL return HTTP 404 or 403 as documented in tasks and SHALL NOT return the other user's data

### Requirement: Unified API envelope for customer operations

Successful and failed customer CRUD responses SHALL use the shared `ApiResult` shape (`success`, optional `data`, optional `message`, optional `code`) consistent with `shared-foundation`.

#### Scenario: Successful create returns envelope

- **WHEN** a create request succeeds
- **THEN** the response body SHALL include `success: true` and the created payload under `data` or equivalent field per existing conventions

### Requirement: Customer records are not user profiles

The customer record CRUD SHALL represent zero or more business customer entities per owner; it SHALL NOT be used as the storage for the single personal profile document described in `user-profile-record`.

#### Scenario: Documentation distinguishes models

- **WHEN** tasks or API documentation describe customer endpoints
- **THEN** they SHALL state that personal profile fields belong to the profile resource, not to generic customer rows, unless explicitly mapped as a product decision

