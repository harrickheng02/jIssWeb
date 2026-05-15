## ADDED Requirements

### Requirement: Report closure triggers a notification to the reporter

When a moderator or administrator transitions a forum report's status to a terminal closed state (`resolved` or `rejected`), the system SHALL create an in-app notification addressed to the original reporter.

The notification SHALL:
- Use `Type = "ReportResolved"`.
- Set `RecipientSubId` to `ForumReportRecord.ReporterSub`.
- Set `PostId` to `ForumReportRecord.PostId` for deep-linking to the related post.
- Set `PostTitle` to the current post title fetched at write time; if the post is not found or has been deleted, `PostTitle` SHALL be set to an empty string.
- Set `ActorSubId` to an empty string (system-generated; moderator identity is not disclosed to the reporter).
- Set `ReportId` to the `ForumReportRecord.Id` (used as idempotency key).
- Set `CreatedAtUtc` to the UTC timestamp of the write operation.

The notification SHALL NOT distinguish between `resolved` and `rejected` outcomes. Both terminal states SHALL produce the same notification type with the same text contract.

The notification write SHALL be fire-and-forget relative to the report status update: if the notification insert fails for a reason other than a duplicate-key conflict, the report status update SHALL NOT be rolled back.

#### Scenario: Status transitions to resolved triggers notification

- **WHEN** an authorized moderator or administrator calls `PATCH /api/mod/reports/{id}` with `{ "status": "resolved" }` on a report with `ReporterSub = reporterA`
- **THEN** the response SHALL be successful
- **AND** a notification record SHALL exist in `forum_in_app_notifications` with `RecipientSubId = reporterA`, `Type = "ReportResolved"`, and `ReportId` equal to the report's id

#### Scenario: Status transitions to rejected triggers notification

- **WHEN** an authorized moderator or administrator calls `PATCH /api/mod/reports/{id}` with `{ "status": "rejected" }` on a report with `ReporterSub = reporterA`
- **THEN** the response SHALL be successful
- **AND** a notification record SHALL exist in `forum_in_app_notifications` with `RecipientSubId = reporterA`, `Type = "ReportResolved"`, and `ReportId` equal to the report's id

#### Scenario: Alias status values also trigger notification

- **WHEN** an authorized caller submits `{ "status": "acknowledged" }` or `{ "status": "dismissed" }`
- **THEN** the persisted notification SHALL exist with `Type = "ReportResolved"` (mapped to canonical `resolved` or `rejected` respectively)

#### Scenario: Status transition to pending does not trigger notification

- **WHEN** an authorized caller calls `PATCH /api/mod/reports/{id}` with `{ "status": "pending" }` to reopen a report
- **THEN** NO new notification SHALL be written for this transition

### Requirement: Report resolved notification is idempotent

The system SHALL NOT produce more than one `ReportResolved` notification for the same report ID, regardless of how many times the report is closed, reopened, and closed again.

The system SHALL enforce this by maintaining a sparse unique index on `ReportId` in the `forum_in_app_notifications` collection. When an insert would violate this constraint, the system SHALL silently skip the notification write (duplicate-key conflict is treated as success, not an error).

#### Scenario: Report reopened and closed again does not duplicate notification

- **WHEN** a report is first closed (status set to `resolved` or `rejected`) and a notification is written
- **AND** the report is subsequently reopened (status set to `pending`)
- **AND** the report is closed again (status set to `resolved` or `rejected`)
- **THEN** exactly one notification record SHALL exist for that `ReportId` in `forum_in_app_notifications`

#### Scenario: Repeated terminal-to-terminal transitions do not duplicate notification

- **WHEN** a report already in `resolved` state is PATCH'd to `rejected` (terminal-to-terminal)
- **THEN** the notification write is silently skipped due to duplicate-key conflict
- **AND** the report status update SHALL succeed

### Requirement: Post title is stored as a snapshot at notification write time

The system SHALL capture the post title at the moment the notification is written and persist it in `PostTitle`. If the post has already been deleted or is otherwise not found, `PostTitle` SHALL be stored as an empty string. The system SHALL NOT query the post title again at read time from the notification endpoint.

#### Scenario: Post exists at notification write time

- **WHEN** a notification is written for a report whose `PostId` refers to an existing non-deleted post
- **THEN** the stored `PostTitle` SHALL equal the post's `Title` at write time

#### Scenario: Post does not exist at notification write time

- **WHEN** a notification is written for a report whose `PostId` does not match any existing post
- **THEN** the stored `PostTitle` SHALL equal an empty string
- **AND** the notification SHALL still be written successfully
