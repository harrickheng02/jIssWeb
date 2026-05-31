## ADDED Requirements

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

## MODIFIED Requirements

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
