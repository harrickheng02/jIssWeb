## ADDED Requirements

### Requirement: ReportResolved notification type is defined and persisted

The system SHALL define `InAppNotificationTypes.ReportResolved = "ReportResolved"` as a valid notification type constant. `InAppNotificationRecord` SHALL include a nullable field `ReportId` (string?) used as the idempotency key for this type. `ForumMongoSetup` SHALL maintain a sparse unique index on `ReportId` in the `forum_in_app_notifications` collection. Documents with `Type != "ReportResolved"` or with a null `ReportId` SHALL NOT be affected by this index.

#### Scenario: Sparse index does not affect ReplyToPost notifications

- **WHEN** a `ReplyToPost` notification is inserted with `ReportId = null`
- **THEN** the insert SHALL succeed without unique constraint violation

#### Scenario: Second ReportResolved notification for same ReportId is rejected

- **WHEN** a `ReportResolved` notification with a given `ReportId` already exists
- **AND** an attempt is made to insert another notification with the same `ReportId`
- **THEN** the insert SHALL fail with a duplicate-key error
- **AND** the calling code SHALL treat this as a silent skip (no error propagated)

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
