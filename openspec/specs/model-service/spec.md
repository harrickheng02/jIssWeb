## ADDED Requirements

### Requirement: Model domain API shell

The model (模型) service SHALL provide a runnable Web API with Swagger in Development and SHALL register MongoDB and Redis clients via dependency injection using service-specific configuration keys.

#### Scenario: Service starts with infrastructure registration

- **WHEN** the application starts with valid connection strings in configuration
- **THEN** resolution of MongoDB and Redis clients SHALL not throw during host build for skeleton registration

### Requirement: JWT validation

The model service SHALL validate Bearer tokens from the user service and SHALL not implement token issuance.

#### Scenario: Invalid token rejected

- **WHEN** a client calls a protected route with an expired or malformed JWT
- **THEN** the response SHALL be 401

### Requirement: Health endpoint

The model service SHALL expose a health check endpoint returning the unified `ApiResult` shape.

#### Scenario: Health success

- **WHEN** a client requests the health endpoint
- **THEN** `success` in the JSON body SHALL be true

### Requirement: Forum posts and replies persistence

The model service SHALL persist forum posts and replies in MongoDB using collections and indexes appropriate for list-by-time and lookup by post id.

#### Scenario: Documents stored with author sub

- **WHEN** a post or reply is created via the forum API
- **THEN** the document SHALL store the author key derived from JWT `sub`

### Requirement: Forum HTTP surface on model service

The model service SHALL implement the forum REST endpoints described in `forum-content-api` at paths under `/api/forum`, with public GET routes and POST routes protected by JWT bearer authentication using the same validation as existing protected routes.

#### Scenario: Protected routes require bearer

- **WHEN** a client calls a forum write endpoint without a valid Bearer token
- **THEN** the response SHALL be 401

#### Scenario: Public read without token

- **WHEN** a client calls forum list or detail GET endpoints without authentication
- **THEN** the request SHALL succeed when the resource exists

### Requirement: Forum search persistence support

The model service SHALL implement persistence and query support for `GET /api/forum/posts` with `q` as specified in `forum-post-search`, including MongoDB filters on post title and `AuthorSubId`, and SHALL add or use indexes appropriate to the chosen query pattern for list-by-search under expected data volumes.

#### Scenario: Search reads stored posts

- **WHEN** a search request is processed against persisted forum posts
- **THEN** the query SHALL read from the same post collection used for the public list
- **AND** author filtering SHALL use the stored `AuthorSubId` field aligned with JWT `sub` per `token-identity-consistency`

### Requirement: In-app notification persistence and indexes

The model service SHALL persist in-app notifications in MongoDB and SHALL create indexes appropriate for listing by recipient and created time, as specified in `in-app-notifications`.

#### Scenario: Notifications stored by recipient sub

- **WHEN** notification documents are written
- **THEN** they SHALL include `RecipientSubId` aligned with JWT `sub` for the intended recipient
- **AND** list queries for the current user SHALL use indexed access patterns on recipient and time fields

### Requirement: In-app notification HTTP surface

The model service SHALL implement the authenticated notification list and read-state endpoints described in `in-app-notifications` under the forum API path prefix used by existing forum routes, with JWT validation consistent with other protected forum routes.

#### Scenario: Protected notification routes

- **WHEN** a client calls notification list or read-state mutation endpoints
- **THEN** invalid or missing Bearer tokens SHALL receive 401
- **AND** successful list responses SHALL be scoped strictly to the caller's `sub`
