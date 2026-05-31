## Purpose

站内通知：收件人与 JWT `sub` 一致、已读未读、列表分页、与论坛回复事件的最小联动；不包含站外通道与站内信会话。
## Requirements
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

### Requirement: Notification list empty and failure handling for UI contract

The notification list capability SHALL be defined such that clients can distinguish an empty result from a failed request (HTTP non-success or unified error contract), to support empty-state and error-state rendering.

#### Scenario: Empty page is valid

- **WHEN** the user has zero notifications and requests the first page
- **THEN** the response SHALL be success with an empty items array or equivalent pagination shape

### Requirement: ReportResolved notification type is defined and persisted

The system SHALL define `InAppNotificationTypes.ReportResolved = "ReportResolved"` as a valid notification type constant. `InAppNotificationRecord` SHALL include a nullable field `ReportId` (string?) used as part of the idempotency key for report-related notification types. `ForumMongoSetup` SHALL maintain a sparse unique compound index on `(ReportId, Type)` in the `forum_in_app_notifications` collection. Documents with a null `ReportId` SHALL NOT be affected by this index. Duplicate inserts for the same `(ReportId, Type)` pair SHALL fail with duplicate-key error and SHALL be treated as silent skip by callers.

#### Scenario: Sparse index does not affect ReplyToPost notifications

- **WHEN** a `ReplyToPost` notification is inserted with `ReportId = null`
- **THEN** the insert SHALL succeed without unique constraint violation

#### Scenario: Second ReportResolved notification for same ReportId is rejected

- **WHEN** a `ReportResolved` notification with a given `ReportId` already exists
- **AND** an attempt is made to insert another notification with the same `ReportId` and `Type=ReportResolved`
- **THEN** the insert SHALL fail with a duplicate-key error
- **AND** the calling code SHALL treat this as a silent skip (no error propagated)

#### Scenario: ReportAcknowledged and ReportResolved share ReportId without conflict

- **WHEN** a `ReportAcknowledged` notification exists for `ReportId=R1`
- **AND** a `ReportResolved` notification is inserted for the same `ReportId=R1`
- **THEN** both documents SHALL exist

### Requirement: Notification list maps ReportResolved type for display

The `GET /api/forum/notifications` response SHALL correctly handle `ReportResolved` type notifications in its DTO mapping:

- `ActorId` SHALL be an empty string.
- `ActorDisplayName` SHALL return the fixed string `"系统"` (not resolved via `ForumAuthorDisplayResolver`, since `ActorSubId` is empty and no user lookup is needed).
- `PostTitle` SHALL reflect the snapshot stored at write time; if empty, the frontend SHALL display `"内容已移除"` (this is a frontend rendering contract, not a backend contract).
- `PostId` SHALL contain the deep-link post ID as stored.
- The `read` field SHALL follow the existing read/unread semantics.

#### Scenario: ReportResolved notification appears in list with system actor

- **WHEN** the current user has a `ReportResolved` notification
- **AND** they request `GET /api/forum/notifications`
- **THEN** the item SHALL have `Type = "ReportResolved"`, `ActorId = ""`, and `ActorDisplayName = "系统"`

#### Scenario: ReportResolved notification with empty PostTitle is returned correctly

- **WHEN** a `ReportResolved` notification has `PostTitle = ""`
- **THEN** the DTO `PostTitle` SHALL be an empty string (frontend renders "内容已移除")

#### Scenario: ReportResolved notification supports mark-read operations

- **WHEN** the caller marks a `ReportResolved` notification as read via `POST /api/forum/notifications/{id}/read`
- **THEN** the notification SHALL be marked read using the existing mark-read semantics

#### Scenario: Unread count includes unread ReportResolved notifications

- **WHEN** the current user has one or more unread `ReportResolved` notifications
- **THEN** `GET /api/forum/notifications/unread-count` SHALL include them in the count

### Requirement: Forum warning sanction notifies the target user

When a warning sanction is successfully created through the moderation flow, the Model service SHALL insert an in-app notification to the sanctioned user's `sub` with `Type = "ForumWarning"`, `ActorSubId` empty (system behavior), and a summary message that a community rule violation was recorded without disclosing moderator identity or report adjudication details.

#### Scenario: Warning creates notification

- **WHEN** a moderator successfully issues `type=warning` against user U
- **THEN** a notification SHALL exist with `RecipientSubId=U` and `Type=ForumWarning`

#### Scenario: Warning notification lists in inbox

- **WHEN** user U fetches their notification list after a warning
- **THEN** the item SHALL render with system actor labeling consistent with other system notifications

### Requirement: ReportAcknowledged notification type is defined and persisted

The system SHALL define `InAppNotificationTypes.ReportAcknowledged = "ReportAcknowledged"` as a valid notification type constant. Documents of this type SHALL use the same `ReportId`, `PostId`, `PostTitle`, and empty `ActorSubId` field conventions as `ReportResolved`.

#### Scenario: ReportAcknowledged coexists with ReportResolved for same report

- **WHEN** both notification types exist for the same `ReportId`
- **THEN** both inserts SHALL succeed under the composite idempotency index

### Requirement: Notification list maps ReportAcknowledged type for display

The `GET /api/forum/notifications` response SHALL handle `ReportAcknowledged` notifications:

- `ActorId` SHALL be an empty string.
- `ActorDisplayName` SHALL return `"系统"`.
- `PostTitle` SHALL reflect the snapshot stored at write time.
- `PostId` SHALL contain the deep-link post ID.

The frontend rendering contract SHALL display text equivalent to「您对《PostTitle》的举报已受理，正在处理」; when `PostTitle` is empty, display「内容已移除」in place of the title segment.

#### Scenario: ReportAcknowledged appears in list with system actor

- **WHEN** the current user has a `ReportAcknowledged` notification
- **AND** they request `GET /api/forum/notifications`
- **THEN** the item SHALL have `Type = "ReportAcknowledged"`, `ActorId = ""`, and `ActorDisplayName = "系统"`

