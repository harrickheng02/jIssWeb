## MODIFIED Requirements

### Requirement: Moderators and admins update report workflow status

The system SHALL expose `PATCH /api/mod/reports/{reportId}` for authenticated moderators and administrators. The request body SHALL be JSON with a **`status`** string. Accepted values SHALL be **`pending`**, **`rejected`**, **`resolved`**, **`dismissed`** (stored as **`rejected`**), **`acknowledged`** (stored as **`resolved`**); other values SHALL yield **`400`** with the uniform error envelope. The handler SHALL apply the mapped stored status **regardless of the report's prior status**, allowing repeat transitions between terminal states and reopen flows into **`pending`**. When **`status`** maps to **`pending`**, the system SHALL clear `handledBySub`, `handledAtUtc`, and `resolutionCode` on the report. When **`status`** maps to **`rejected`** or **`resolved`**, the system SHALL set `handledBySub` to the caller's **`sub`**, set `handledAtUtc`, and clear `resolutionCode` on the report for this workflow. On a successful transition into a terminal closed state (`resolved` or `rejected`), the system SHALL append a moderation audit record with `targetType=report`, `targetId` equal to the report id, action `report.resolve` or `report.reject` respectively, and metadata including `reportId`, `postId`, and `boardId` derived from the report. Idempotent HTTP retries that would duplicate the same terminal transition audit (same action and same `HandledAtUtc` as already recorded for that report) SHALL NOT create a second audit row. Reopening and closing again SHALL create a new audit row when `HandledAtUtc` changes. Audit write failure SHALL NOT fail the PATCH response. Administrators MAY update any report. Moderators SHALL succeed only when the report's **`boardId`** satisfies their moderator scope; otherwise **`403`**. Missing report **`404`**.

After a successful status update to a terminal closed state (`resolved` or `rejected`), the system SHALL attempt to write a `ReportResolved` in-app notification to the reporter per the `report-resolved-notification` capability. This notification write SHALL be idempotent (duplicate-key conflict is silently ignored) and SHALL NOT cause the PATCH response to fail.

Post and reply deletion SHALL use **`forum-moderation-delete-content`** **`DELETE`** endpoints from moderation surfaces: **post detail** and **expanded report-queue rows** where the same governance controls are exposed (shared component or equivalent). This **`PATCH`** SHALL update report workflow status only.

#### Scenario: Moderator sets status inside board scope

- **WHEN** a moderator calls `PATCH /api/mod/reports/{reportId}` with `{ "status": "rejected" }` for a report whose board is inside their moderator scope
- **THEN** the response SHALL be successful
- **AND** the stored status SHALL equal `rejected`
- **AND** `handledBySub` SHALL equal the moderator's `sub`
- **AND** a `ReportResolved` notification SHALL be written for the report's `ReporterSub`
- **AND** a moderation audit row with action `report.reject` and `Metadata.postId` SHALL exist

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

### Requirement: Moderators and admins can acknowledge pending reports

The system SHALL expose `POST /api/mod/reports/{reportId}/acknowledge` for authenticated moderators and administrators. The handler SHALL succeed only when the report's canonical status is `pending`. On success the system SHALL set `AcknowledgedAtUtc` to the current UTC time and `AcknowledgedBySub` to the caller's `sub`, SHALL NOT change persisted workflow `status` (which remains `pending`), SHALL attempt to write a `ReportAcknowledged` notification per `report-acknowledged-notification`, SHALL append at most one moderation audit record with action `report.acknowledge`, `targetType=report`, `targetId` equal to the report id, and metadata including `reportId`, `postId`, and `boardId`, and SHALL return the updated list item DTO including acknowledge fields. Duplicate acknowledge requests SHALL NOT create duplicate audit rows. Audit write failure SHALL NOT fail the acknowledge response. Administrators MAY acknowledge any report. Moderators SHALL succeed only when the report's `boardId` is within their moderator scope; otherwise HTTP **403**. Missing report **404**. Non-pending report **400**.

#### Scenario: Moderator acknowledges pending report in scope

- **WHEN** a moderator calls `POST /api/mod/reports/{reportId}/acknowledge` for a pending report in their board scope
- **THEN** the response SHALL be successful
- **AND** `status` SHALL remain `pending`
- **AND** `acknowledgedAtUtc` and `acknowledgedBySub` SHALL be set
- **AND** a moderation audit row with action `report.acknowledge` SHALL exist

#### Scenario: Acknowledge forbidden out of scope

- **WHEN** a moderator calls acknowledge for a report outside their board scope
- **THEN** the response SHALL be HTTP 403

#### Scenario: Acknowledge on closed report rejected

- **WHEN** acknowledge is called on a report with canonical status `resolved` or `rejected`
- **THEN** the response SHALL be HTTP 400

#### Scenario: Repeated acknowledge is idempotent for audit

- **WHEN** acknowledge succeeds twice for the same pending report due to client retry
- **THEN** at most one `report.acknowledge` audit row SHALL exist for that report id
