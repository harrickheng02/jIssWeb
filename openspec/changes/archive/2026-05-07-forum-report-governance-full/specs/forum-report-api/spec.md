## MODIFIED Requirements

### Requirement: Moderators and admins update report workflow status

The system SHALL expose `PATCH /api/mod/reports/{reportId}` for authenticated moderators and administrators. The request body SHALL be JSON with a **`status`** string. Accepted values SHALL be **`pending`**, **`rejected`**, **`resolved`**, **`dismissed`** (stored as **`rejected`**), **`acknowledged`** (stored as **`resolved`**); other values SHALL yield **`400`** with the uniform error envelope. The handler SHALL apply the mapped stored status **regardless of the report's prior status**, allowing repeat transitions between terminal states and reopen flows into **`pending`**. When **`status`** maps to **`pending`**, the system SHALL clear `handledBySub`, `handledAtUtc`, and `resolutionCode` on the report. When **`status`** maps to **`rejected`** or **`resolved`**, the system SHALL set `handledBySub` to the caller's **`sub`**, set `handledAtUtc`, and clear `resolutionCode` on the report for this workflow. **`PATCH`** success SHALL persist only the **`forum_reports`** document update and SHALL NOT append moderation audit rows for report status churn. Administrators MAY update any report. Moderators SHALL succeed only when the report's **`boardId`** satisfies their moderator scope; otherwise **`403`**. Missing report **`404`**.

Post and reply deletion SHALL use **`forum-moderation-delete-content`** **`DELETE`** endpoints from moderation surfaces: **post detail** and **expanded report-queue rows** where the same governance controls are exposed (shared component or equivalent). This **`PATCH`** SHALL update report workflow status only.

#### Scenario: Moderator sets status inside board scope

- **WHEN** a moderator calls `PATCH /api/mod/reports/{reportId}` with `{ "status": "rejected" }` for a report whose board is inside their moderator scope
- **THEN** the response SHALL be successful
- **AND** the stored status SHALL equal `rejected`
- **AND** `handledBySub` SHALL equal the moderator's `sub`

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

### Requirement: Moderators and admins can list forum reports within scope

The system SHALL expose `GET /api/mod/reports` for authenticated clients whose effective forum role is moderator or admin per `token-identity-consistency`. The response SHALL be paginated using query parameters aligned with existing forum moderation list conventions (for example `page`, `pageSize`, and optional `status`). Administrators SHALL receive reports across all boards. Moderators SHALL receive only reports whose persisted board identifier is in the caller's moderator scope when that scope is constrained; moderator authorization SHALL follow the same board-scope rules used for moderator-only post operations in `forum-moderation-post-ops`. Query parameter `status` when present SHALL accept buckets `pending`, `rejected`, or `resolved`, where **`rejected` matches stored `rejected` and legacy `dismissed`**, and **`resolved` matches stored `resolved` and legacy `acknowledged`**. List item payloads SHALL expose canonical `status` after that mapping. List item payloads SHALL **omit** a `resolutionCode` field in the public API contract. Callers without sufficient forum role SHALL receive HTTP 403.

#### Scenario: Admin lists reports

- **WHEN** a client with effective forum role `admin` requests `GET /api/mod/reports`
- **THEN** the response SHALL be successful
- **AND** items MAY include reports from any board

#### Scenario: Moderator lists only scoped reports

- **WHEN** a client with effective forum role `moderator` requests `GET /api/mod/reports` with scope limited to certain boards
- **THEN** every returned item SHALL have board scope compatible with moderator authorization for that board

#### Scenario: Member cannot list reports

- **WHEN** a client with effective forum role `member` requests `GET /api/mod/reports`
- **THEN** the response SHALL be HTTP 403 with the uniform error envelope
