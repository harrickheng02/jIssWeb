## Purpose

站内通知：收件人与 JWT `sub` 一致、已读未读、列表分页、与论坛回复事件的最小联动；不包含站外通道与站内信会话。

## ADDED Requirements

### Requirement: Notification recipient identity

The system SHALL persist each in-app notification with a recipient key `RecipientSubId` that MUST equal the intended recipient's user identifier, and that identifier MUST match JWT `sub` and user-service primary key semantics per `openspec/specs/token-identity-consistency`.

#### Scenario: Recipient is not client-supplied for list

- **WHEN** a client requests the current user's notification list or read-state mutations
- **THEN** the effective recipient filter SHALL use only `sub` from the validated token
- **AND** the service SHALL NOT honor a client-provided user id that differs from `sub` for selecting rows

### Requirement: Reply event creates a notification for post author

When a reply is successfully created on a post, the system SHALL create an in-app notification addressed to the post author's stored author key when that key is not equal to the reply author's `sub`.

#### Scenario: Another user replies

- **WHEN** an authenticated user creates a reply on a post whose persisted author key differs from the reply author's `sub`
- **THEN** the system SHALL persist a notification whose `RecipientSubId` equals the post author's key and whose payload references the post and reply per implementation contract

#### Scenario: Author replies to own post

- **WHEN** the reply author's `sub` equals the post author's stored key
- **THEN** the system SHALL NOT create a self-addressed reply notification for that event

### Requirement: Authenticated notification list with pagination

The system SHALL expose an authenticated HTTP endpoint that returns the current user's notifications in reverse chronological order with stable pagination parameters documented in the implementation contract.

#### Scenario: Authenticated list

- **WHEN** a client with a valid Bearer token requests the notification list with valid pagination parameters
- **THEN** the response SHALL include only notifications whose `RecipientSubId` equals the caller's `sub`
- **AND** each item SHALL expose read state and fields needed to render a summary and deep link to the related post or reply

#### Scenario: Unauthenticated list rejected

- **WHEN** a client without a valid token requests the notification list
- **THEN** the response SHALL be 401

### Requirement: Mark notification read state

The system SHALL expose authenticated operations to mark one notification as read and to mark all notifications for the current user as read, with idempotent behavior when applied repeatedly.

#### Scenario: Mark one read

- **WHEN** the caller marks a notification id that belongs to the current user as read
- **THEN** subsequent reads of that row SHALL indicate read state
- **AND** attempting to mark another user's notification SHALL not leak existence (403 or 404 per uniform policy)

#### Scenario: Mark all read

- **WHEN** the caller requests mark-all-read for the current user
- **THEN** all that user's notifications SHALL be read state

### Requirement: Notification list empty and failure handling for UI contract

The notification list capability SHALL be defined such that clients can distinguish an empty result from a failed request (HTTP non-success or unified error contract), to support empty-state and error-state rendering.

#### Scenario: Empty page is valid

- **WHEN** the user has zero notifications and requests the first page
- **THEN** the response SHALL be success with an empty items array or equivalent pagination shape
