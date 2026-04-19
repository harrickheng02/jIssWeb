## MODIFIED Requirements

### Requirement: Authenticated notification list with pagination

The system SHALL expose an authenticated HTTP endpoint that returns the current user's notifications in reverse chronological order with stable pagination parameters documented in the implementation contract.

#### Scenario: Authenticated list

- **WHEN** a client with a valid Bearer token requests the notification list with valid pagination parameters
- **THEN** the response SHALL include only notifications whose `RecipientSubId` equals the caller's `sub`
- **AND** each item SHALL expose read state and fields needed to render a summary and deep link to the related post or reply
- **AND** the list ordering SHALL be stable for incremental client refresh by sorting by `(CreatedAtUtc desc, Id desc)` or an equivalent stable key

#### Scenario: Unauthenticated list rejected

- **WHEN** a client without a valid token requests the notification list
- **THEN** the response SHALL be 401

### Requirement: Mark notification read state

The system SHALL expose authenticated operations to mark one notification as read and to mark all notifications for the current user as read, with idempotent behavior when applied repeatedly.

#### Scenario: Mark one read

- **WHEN** the caller marks a notification id that belongs to the current user as read
- **THEN** subsequent reads of that row SHALL indicate read state
- **AND** the read timestamp SHALL remain the first-read timestamp for that row when the operation is applied repeatedly
- **AND** attempting to mark another user's notification SHALL not leak existence (403 or 404 per uniform policy)

#### Scenario: Mark all read

- **WHEN** the caller requests mark-all-read for the current user
- **THEN** all that user's notifications SHALL be read state
- **AND** each row's read timestamp SHALL remain the first-read timestamp when the operation is applied repeatedly

