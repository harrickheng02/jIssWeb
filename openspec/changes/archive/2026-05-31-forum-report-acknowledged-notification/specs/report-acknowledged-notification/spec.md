## ADDED Requirements

### Requirement: Report acknowledge triggers notification to reporter

When an authorized moderator or administrator acknowledges a pending forum report, the system SHALL create an in-app notification addressed to the original reporter.

The notification SHALL:
- Use `Type = "ReportAcknowledged"`.
- Set `RecipientSubId` to `ForumReportRecord.ReporterSub`.
- Set `PostId` to `ForumReportRecord.PostId` for deep-linking.
- Set `PostTitle` to the current post title at write time; if the post is not found or deleted, `PostTitle` SHALL be an empty string.
- Set `ActorSubId` to an empty string (system-generated; moderator identity SHALL NOT be disclosed).
- Set `ReportId` to `ForumReportRecord.Id` (idempotency key scoped with `Type`).
- Set `CreatedAtUtc` to the UTC timestamp of the write operation.

The notification write SHALL be fire-and-forget relative to the acknowledge operation: duplicate-key conflict SHALL be silently skipped; other insert failures SHALL NOT roll back the acknowledge metadata update on the report.

#### Scenario: Acknowledge pending report triggers notification

- **WHEN** an authorized caller successfully invokes acknowledge on a report with `status=pending` and `ReporterSub=reporterA`
- **THEN** a notification SHALL exist with `RecipientSubId=reporterA`, `Type=ReportAcknowledged`, and `ReportId` equal to the report id

#### Scenario: Repeated acknowledge does not duplicate notification

- **WHEN** acknowledge is invoked twice on the same report
- **THEN** exactly one `ReportAcknowledged` notification SHALL exist for that `ReportId`

#### Scenario: Acknowledge on non-pending report rejected

- **WHEN** acknowledge is invoked on a report whose canonical status is not `pending`
- **THEN** the response SHALL be HTTP 400 with a documented error code

### Requirement: Report acknowledge notification is distinct from closure notification

The system SHALL allow both `ReportAcknowledged` and `ReportResolved` notifications to exist for the same `ReportId` when the report lifecycle includes acknowledge followed by terminal closure.

#### Scenario: Acknowledged then closed produces two notifications

- **WHEN** a report is acknowledged while pending
- **AND** later closed via `PATCH` to `resolved` or `rejected`
- **THEN** exactly one `ReportAcknowledged` and one `ReportResolved` notification SHALL exist for that `ReportId`
