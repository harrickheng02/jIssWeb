## MODIFIED Requirements

### Requirement: Moderators and admins update report workflow status

The system SHALL expose `PATCH /api/mod/reports/{reportId}` for authenticated moderators and administrators. The request body SHALL be JSON with a **`status`** string. Accepted values SHALL be **`pending`**, **`rejected`**, **`resolved`**, **`dismissed`** (stored as **`rejected`**), **`acknowledged`** (stored as **`resolved`**); other values SHALL yield **`400`** with the uniform error envelope. The handler SHALL apply the mapped stored status **regardless of the report's prior status**, allowing repeat transitions between terminal states and reopen flows into **`pending`**. When **`status`** maps to **`pending`**, the system SHALL clear `handledBySub`, `handledAtUtc`, and `resolutionCode` on the report. When **`status`** maps to **`rejected`** or **`resolved`**, the system SHALL set `handledBySub` to the caller's **`sub`**, set `handledAtUtc`, and clear `resolutionCode` on the report for this workflow. **`PATCH`** success SHALL persist only the **`forum_reports`** document update and SHALL NOT append moderation audit rows for report status churn. Administrators MAY update any report. Moderators SHALL succeed only when the report's **`boardId`** satisfies their moderator scope; otherwise **`403`**. Missing report **`404`**.

After a successful status update to a terminal closed state (`resolved` or `rejected`), the system SHALL attempt to write a `ReportResolved` in-app notification to the reporter per the `report-resolved-notification` capability. This notification write SHALL be idempotent (duplicate-key conflict is silently ignored) and SHALL NOT cause the PATCH response to fail.

Post and reply deletion SHALL use **`forum-moderation-delete-content`** **`DELETE`** endpoints from moderation surfaces: **post detail** and **expanded report-queue rows** where the same governance controls are exposed (shared component or equivalent). This **`PATCH`** SHALL update report workflow status only.

#### Scenario: Moderator sets status inside board scope

- **WHEN** a moderator calls `PATCH /api/mod/reports/{reportId}` with `{ "status": "rejected" }` for a report whose board is inside their moderator scope
- **THEN** the response SHALL be successful
- **AND** the stored status SHALL equal `rejected`
- **AND** `handledBySub` SHALL equal the moderator's `sub`
- **AND** a `ReportResolved` notification SHALL be written for the report's `ReporterSub`

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
