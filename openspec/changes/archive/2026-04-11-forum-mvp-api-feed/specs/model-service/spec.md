## ADDED Requirements

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
