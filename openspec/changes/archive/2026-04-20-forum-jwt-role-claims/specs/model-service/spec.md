## ADDED Requirements

### Requirement: Forum governance routes enforce global forum role

The model service SHALL validate the `forumRole` claim on the access token for routes designated as moderator-only or admin-only under the forum HTTP surface. Moderator-only routes SHALL allow requests only when the effective forum role is `moderator` or `admin`. Admin-only routes SHALL allow requests only when the effective forum role is `admin`. Effective forum role SHALL be determined per `token-identity-consistency` (including default `member` when `forumRole` is omitted). Missing or invalid Bearer tokens SHALL continue to receive HTTP 401 per existing JWT validation. Authenticated callers whose role is insufficient SHALL receive HTTP 403 with the service's uniform error envelope.

#### Scenario: Moderator-only route allows moderator

- **WHEN** a client calls a moderator-only forum route with a valid Bearer token whose effective forum role is `moderator`
- **THEN** the response SHALL NOT be 401 solely due to forum role
- **AND** if other authorization checks pass, the response SHALL not be 403 solely due to forum role

#### Scenario: Moderator-only route allows admin

- **WHEN** a client calls a moderator-only forum route with a valid Bearer token whose effective forum role is `admin`
- **THEN** the response SHALL NOT be 403 solely due to forum role

#### Scenario: Moderator-only route rejects member

- **WHEN** a client calls a moderator-only forum route with a valid Bearer token whose effective forum role is `member`
- **THEN** the response SHALL be HTTP 403 with the uniform error contract

#### Scenario: Admin-only route rejects moderator

- **WHEN** a client calls an admin-only forum route with a valid Bearer token whose effective forum role is `moderator`
- **THEN** the response SHALL be HTTP 403 with the uniform error contract

#### Scenario: Admin-only route allows admin

- **WHEN** a client calls an admin-only forum route with a valid Bearer token whose effective forum role is `admin`
- **THEN** the response SHALL not be 403 solely due to forum role

#### Scenario: Demonstration routes exist

- **WHEN** the implementation is complete for this capability
- **THEN** at least one moderator-only and one admin-only forum route (or documented placeholder) SHALL exist so that integration tests can assert 403 for `member` tokens
