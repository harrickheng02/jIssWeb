# forum-report-api Specification

## Purpose

定义论坛举报在 Model.Api 侧的持久化、鉴权边界与 REST 契约；**已结案单据**在 **`forum_reports`** 中按配置 **`Forum:ReportRetention`** 周期清理，**`forum_moderation_audit`** 另计。管理端列表与 `PATCH` 使用规范状态 **`pending` / `rejected` / `resolved`**；存量 `dismissed` / `acknowledged` 在查询过滤、列表展示与 `PATCH` 请求体中分别视为 **`rejected` / `resolved`**。**举报工单状态的 `PATCH` 不写入** **`forum_moderation_audit`**（与内容类治理审计分离）。**硬删除帖子或回复** 由 **`forum-moderation-delete-content`** 的 `DELETE` 端点完成，并写入对应审计动作。
## Requirements
### Requirement: Authenticated user can submit a forum report for a post or reply

The system SHALL expose `POST /api/forum/reports` for authenticated clients to create a report targeting either a forum post or a forum reply. The request body SHALL include `targetType` with value exactly `post` or `reply`, `targetId` identifying the persisted target, and MAY include `reason` as a short string. The system SHALL persist the reporter identity from JWT `sub`, the resolved board scope for authorization filtering, timestamps, and an initial report `status` of `pending`. The system SHALL reject unauthenticated callers with HTTP 401 and the uniform error envelope.

#### Scenario: Member submits report for existing post

- **WHEN** a client with a valid Bearer token calls `POST /api/forum/reports` with `targetType=post` and `targetId` equal to an existing non-deleted post id
- **THEN** the response SHALL be successful
- **AND** a new report record SHALL exist with `status=pending` and `reporterSub` equal to the caller's `sub`

#### Scenario: Member submits report for existing reply

- **WHEN** a client with a valid Bearer token calls `POST /api/forum/reports` with `targetType=reply` and `targetId` equal to an existing non-deleted reply id
- **THEN** the response SHALL be successful
- **AND** a new report record SHALL exist with `status=pending` and correct target references

#### Scenario: Unauthenticated submit rejected

- **WHEN** a client without a valid Bearer token calls `POST /api/forum/reports`
- **THEN** the response SHALL be HTTP 401 with the uniform error envelope

#### Scenario: Missing or invalid target rejected

- **WHEN** an authenticated client calls `POST /api/forum/reports` for a deleted or unknown target
- **THEN** the response SHALL be HTTP 404 with the uniform error envelope

#### Scenario: Duplicate pending report rejected

- **WHEN** an authenticated client calls `POST /api/forum/reports` and a report already exists with the same `reporterSub`, `targetType`, `targetId`, and `status=pending`
- **THEN** the response SHALL be HTTP 409 with the uniform error envelope and a documented error code

### Requirement: Moderators and admins can list forum reports within scope

The system SHALL expose `GET /api/mod/reports` for authenticated clients whose effective forum role is moderator or admin per `token-identity-consistency`. The response SHALL be paginated using query parameters aligned with existing forum moderation list conventions (`page`, `pageSize`, optional `status`). Administrators SHALL receive reports across all boards. Moderators SHALL receive only reports whose persisted board identifier is in the caller's moderator scope when that scope is constrained; moderator authorization SHALL follow the same board-scope rules used for moderator-only post operations in `forum-moderation-post-ops`. Query parameter `status` when present SHALL accept buckets `pending`, `rejected`, or `resolved`, where **`rejected`** matches stored `rejected` and legacy `dismissed`, and **`resolved`** matches stored `resolved` and legacy `acknowledged`. List item payloads SHALL expose canonical `status` after that mapping, reporter and target fields, board labeling, timestamps, and `handledBySub` / `handledAtUtc` when set. List item payloads SHALL also expose nullable `acknowledgedAtUtc` and `acknowledgedBySub` when an acknowledge action has occurred while the report remains pending. List item payloads SHALL **omit** a `resolutionCode` field in the public API contract. Callers without sufficient forum role SHALL receive HTTP 403.

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

#### Scenario: Acknowledged pending report exposes acknowledge metadata

- **WHEN** a pending report has been acknowledged
- **THEN** the list item SHALL include non-null `acknowledgedAtUtc` and `acknowledgedBySub`
- **AND** canonical `status` SHALL remain `pending`

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

### Requirement: Closed forum report documents expire from primary storage

The system SHALL periodically remove **`forum_reports`** documents whose stored **`status`** is a terminal closed bucket (**`rejected`** / **`resolved`**, **including legacy `dismissed` / `acknowledged`**) and whose **`HandledAtUtc`** is **strictly older** than a configured retention horizon in UTC days. **`pending`** rows SHALL remain. Retention **`DeleteMany`** SHALL target **`forum_reports`** only; **`forum_moderation_audit`** rows remain until handled by separate archival or retention policies. Scheduling, enable switch, **`ClosedRetentionDays`**, **`IntervalHours`**, and startup delay SHALL be configurable under **`Forum:ReportRetention`** in application configuration.

#### Scenario: Expired closed reports are removed

- **WHEN** the retention background job runs
- **AND** there exist `forum_reports` documents with a terminal status whose `HandledAtUtc` is older than the configured `ClosedRetentionDays` horizon
- **THEN** those documents SHALL be deleted from `forum_reports`
- **AND** `pending` documents SHALL remain unaffected

### Requirement: Moderators and admins can acknowledge pending reports

The system SHALL expose `POST /api/mod/reports/{reportId}/acknowledge` for authenticated moderators and administrators. The handler SHALL succeed only when the report's canonical status is `pending`. On success the system SHALL set `AcknowledgedAtUtc` to the current UTC time and `AcknowledgedBySub` to the caller's `sub`, SHALL NOT change persisted workflow `status` (which remains `pending`), SHALL attempt to write a `ReportAcknowledged` notification per `report-acknowledged-notification`, and SHALL return the updated list item DTO including acknowledge fields. Administrators MAY acknowledge any report. Moderators SHALL succeed only when the report's `boardId` is within their moderator scope; otherwise HTTP **403**. Missing report **404**. Non-pending report **400**.

#### Scenario: Moderator acknowledges pending report in scope

- **WHEN** a moderator calls `POST /api/mod/reports/{reportId}/acknowledge` for a pending report in their board scope
- **THEN** the response SHALL be successful
- **AND** `status` SHALL remain `pending`
- **AND** `acknowledgedAtUtc` and `acknowledgedBySub` SHALL be set

#### Scenario: Acknowledge forbidden out of scope

- **WHEN** a moderator calls acknowledge for a report outside their board scope
- **THEN** the response SHALL be HTTP 403

#### Scenario: Acknowledge on closed report rejected

- **WHEN** acknowledge is called on a report with canonical status `resolved` or `rejected`
- **THEN** the response SHALL be HTTP 400

