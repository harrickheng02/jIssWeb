## ADDED Requirements

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
