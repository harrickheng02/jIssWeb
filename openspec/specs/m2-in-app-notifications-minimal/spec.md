# m2-in-app-notifications-minimal Specification

## Purpose
TBD - created by archiving change m2-in-app-notifications-minimal. Update Purpose after archive.
## Requirements
### Requirement: Notification list item deep link contract
The system SHALL return each notification list item with deep link fields sufficient to navigate to the related forum content.

#### Scenario: Reply-to-post notification links to post and reply
- **WHEN** the system returns a notification created from a reply-to-post event
- **THEN** the item SHALL include `PostId`
- **AND** the item SHALL include `ReplyId`

#### Scenario: Notification list item has renderable summary
- **WHEN** the system returns a notification list item
- **THEN** the item SHALL include a human-readable summary payload sufficient for UI rendering, including `PostTitle` and the actor's identity reference (`ActorSubId` or equivalent)

### Requirement: Notification list supports unread-only filter
The system SHALL allow the caller to request only unread notifications in the notification list endpoint.

#### Scenario: Unread-only list
- **WHEN** the caller requests the notification list with an unread-only parameter
- **THEN** the response SHALL include only notifications that are unread (`ReadAtUtc` is null or equivalent)

### Requirement: Notification unread count endpoint
The system SHALL expose an authenticated endpoint that returns the unread notification count for the current user.

#### Scenario: Unread count reflects unread rows
- **WHEN** an authenticated caller requests the unread notification count
- **THEN** the response SHALL equal the number of notifications for the caller where read state is unread

### Requirement: Reply notification write is idempotent per reply id
The system SHALL prevent duplicate reply-to-post notifications from being persisted for the same reply event.

#### Scenario: Duplicate write attempt
- **WHEN** the system attempts to persist a second reply-to-post notification for the same `ReplyId`
- **THEN** the system SHALL store at most one notification for that `ReplyId`

