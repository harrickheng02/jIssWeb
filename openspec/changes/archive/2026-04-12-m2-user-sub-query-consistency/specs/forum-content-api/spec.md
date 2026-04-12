## ADDED Requirements

### Requirement: Current user's posts list

The system SHALL expose an authenticated HTTP endpoint that returns a paginated list of post summaries for which the persisted author key equals JWT `sub`, using the same summary field contract as the public posts list where applicable.

#### Scenario: Authenticated user retrieves own posts

- **WHEN** a client presents a valid Bearer token and requests the current user's posts list with supported pagination parameters
- **THEN** each item SHALL include identifiers, title, excerpt, author identity field equal to `sub`, published time, board, tags, and counters consistent with the public list contract
- **AND** items SHALL be only posts whose stored author key equals `sub`

#### Scenario: Invalid pagination on own posts list

- **WHEN** a client sends invalid page or page size parameters to the current user's posts list
- **THEN** the response SHALL use the unified error contract with a non-success HTTP status

#### Scenario: Unauthenticated access to own posts list

- **WHEN** a client without a valid token requests the current user's posts list
- **THEN** the response SHALL be 401

### Requirement: Current user's replies list

The system SHALL expose an authenticated HTTP endpoint that returns a paginated list of replies whose persisted author key equals JWT `sub`, including identifiers, post reference, body preview or full body per implementation contract, and timestamps.

#### Scenario: Authenticated user retrieves own replies

- **WHEN** a client presents a valid Bearer token and requests the current user's replies list with supported pagination parameters
- **THEN** each item SHALL relate to a reply whose stored author key equals `sub`

#### Scenario: Invalid pagination on own replies list

- **WHEN** a client sends invalid page or page size parameters to the current user's replies list
- **THEN** the response SHALL use the unified error contract with a non-success HTTP status

#### Scenario: Unauthenticated access to own replies list

- **WHEN** a client without a valid token requests the current user's replies list
- **THEN** the response SHALL be 401

### Requirement: My-content endpoints align with identity spec

The forum API SHALL implement current-user post and reply list endpoints such that filtering uses only JWT `sub` and SHALL conform to `openspec/specs/token-identity-consistency` for user-scoped reads.

#### Scenario: No alternate user key on my-content lists

- **WHEN** the current user's posts or replies list is requested
- **THEN** the service SHALL NOT accept a distinct user identifier from the client as the primary filter when it differs from `sub`
