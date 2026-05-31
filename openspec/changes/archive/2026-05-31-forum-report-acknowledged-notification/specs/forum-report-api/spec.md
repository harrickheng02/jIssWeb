## ADDED Requirements

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

## MODIFIED Requirements

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
