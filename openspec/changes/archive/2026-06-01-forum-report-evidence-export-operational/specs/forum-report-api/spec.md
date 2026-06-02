## MODIFIED Requirements

### Requirement: Closed forum report documents expire from primary storage

The system SHALL periodically remove **`forum_reports`** documents whose stored **`status`** is a terminal closed bucket (**`rejected`** / **`resolved`**, **including legacy `dismissed` / **`acknowledged`**) and whose **`HandledAtUtc`** is **strictly older** than a configured retention horizon in UTC days. **`pending`** rows SHALL remain. Retention **`DeleteMany`** SHALL target **`forum_reports`** and matching **`forum_report_evidence_snapshots`** documents whose **`HandledAtUtc`** is strictly older than the same horizon; **`forum_moderation_audit`** rows remain until handled by separate archival or retention policies. Scheduling, enable switch, **`ClosedRetentionDays`**, **`IntervalHours`**, and startup delay SHALL be configurable under **`Forum:ReportRetention`** in application configuration.

#### Scenario: Expired closed reports are removed

- **WHEN** the retention background job runs
- **AND** there exist `forum_reports` documents with a terminal status whose `HandledAtUtc` is older than the configured `ClosedRetentionDays` horizon
- **THEN** those documents SHALL be deleted from `forum_reports`
- **AND** `pending` documents SHALL remain unaffected

#### Scenario: Expired evidence snapshots are removed in the same job

- **WHEN** the retention background job runs
- **AND** there exist `forum_report_evidence_snapshots` documents whose `HandledAtUtc` is older than the configured `ClosedRetentionDays` horizon
- **THEN** those snapshot documents SHALL be deleted
- **AND** `forum_moderation_audit` documents SHALL remain unaffected

### Requirement: Moderators and admins update report workflow status

The system SHALL expose `PATCH /api/mod/reports/{reportId}` for authenticated moderators and administrators. The request body SHALL be JSON with a **`status`** string. Accepted values SHALL be **`pending`**, **`rejected`**, **`resolved`**, **`dismissed`** (stored as **`rejected`**), **`acknowledged`** (stored as **`resolved`**); other values SHALL yield **`400`** with the uniform error envelope. The handler SHALL apply the mapped stored status **regardless of the report's prior status**, allowing repeat transitions between terminal states and reopen flows into **`pending`**. When **`status`** maps to **`pending`**, the system SHALL clear `handledBySub`, `handledAtUtc`, and `resolutionCode` on the report. When **`status`** maps to **`rejected`** or **`resolved`**, the system SHALL set `handledBySub` to the caller's **`sub`**, set `handledAtUtc`, and clear `resolutionCode` on the report for this workflow. On a successful transition into a terminal closed state (`resolved` or `rejected`), the system SHALL append a moderation audit record with `targetType=report`, `targetId` equal to the report id, action `report.resolve` or `report.reject` respectively, and metadata including `reportId`, `postId`, and `boardId` derived from the report, and SHALL attempt to write an evidence snapshot per `forum-report-evidence-export`. Idempotent HTTP retries that would duplicate the same terminal transition audit (same action and same `HandledAtUtc` as already recorded for that report) SHALL NOT create a second audit row. Reopening and closing again SHALL create a new audit row when `HandledAtUtc` changes. Audit write failure SHALL NOT fail the PATCH response. Evidence snapshot write failure SHALL NOT fail the PATCH response. Administrators MAY update any report. Moderators SHALL succeed only when the report's **`boardId`** satisfies their moderator scope; otherwise **`403`**. Missing report **`404`**.

After a successful status update to a terminal closed state (`resolved` or `rejected`), the system SHALL attempt to write a `ReportResolved` in-app notification to the reporter per the `report-resolved-notification` capability. This notification write SHALL be idempotent (duplicate-key conflict is silently ignored) and SHALL NOT cause the PATCH response to fail.

Post and reply deletion SHALL use **`forum-moderation-delete-content`** **`DELETE`** endpoints from moderation surfaces: **post detail** and **expanded report-queue rows** where the same governance controls are exposed (shared component or equivalent). This **`PATCH`** SHALL update report workflow status only.

#### Scenario: Moderator sets status inside board scope

- **WHEN** a moderator calls `PATCH /api/mod/reports/{reportId}` with `{ "status": "rejected" }` for a report whose board is inside their moderator scope
- **THEN** the response SHALL be successful
- **AND** the stored status SHALL equal `rejected`
- **AND** `handledBySub` SHALL equal the moderator's `sub`
- **AND** a `ReportResolved` notification SHALL be written for the report's `ReporterSub`
- **AND** a moderation audit row with action `report.reject` and `Metadata.postId` SHALL exist
- **AND** an evidence snapshot SHALL exist for that report and `HandledAtUtc`

#### Scenario: Moderator forbidden out of scope

- **WHEN** a moderator calls `PATCH /api/mod/reports/{reportId}` with a valid `status` for a report whose board lies outside their scope
- **THEN** the response SHALL be HTTP 403 with the uniform error envelope

#### Scenario: Authorized caller transitions between terminal states or reopens

- **WHEN** an authorized moderator or administrator calls **`PATCH`** on a report that is already **`rejected`** or **`resolved`** and supplies a **`status`** that differs from its current canonical bucket (`pending`, `rejected`, or `resolved`)
- **THEN** the response SHALL succeed when scope allows
- **AND** the persisted report SHALL reflect the newly requested canonical status and updated handler fields consistent with rules above

#### Scenario: Alias status values normalize to canonical stores

- **WHEN** a caller submits `{ "status": "dismissed" }`
- **THEN** the persisted status SHALL equal `rejected`
- **WHEN** a caller submits `{ "status": "acknowledged" }`
- **THEN** the persisted status SHALL equal `resolved`

#### Scenario: Reopening a report does not trigger a notification

- **WHEN** an authorized caller calls `PATCH /api/mod/reports/{reportId}` with `{ "status": "pending" }` to reopen a report
- **THEN** the response SHALL succeed
- **AND** NO new `ReportResolved` notification SHALL be written
