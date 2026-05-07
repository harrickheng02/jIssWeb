# forum-report-api Specification

## Purpose

定义论坛举报在 Model.Api 侧的持久化、鉴权边界与 REST 契约，支撑 `pm-plan` Issue #4 最小闭环；与 `token-identity-consistency`、`forum-moderation-post-ops` 中的版区间规则一致。**合入后契约以仓库根目录 `openspec/specs/forum-report-api/spec.md` 为准。**

## ADDED Requirements

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

The system SHALL expose `GET /api/mod/reports` for authenticated clients whose effective forum role is moderator or admin per `token-identity-consistency`. The response SHALL be paginated using query parameters aligned with existing forum moderation list conventions (for example `page`, `pageSize`, optional `status`). Administrators SHALL receive reports across all boards. Moderators SHALL receive only reports whose persisted board identifier is in the caller's moderator scope; moderator authorization SHALL follow the same board-scope rules used for moderator-only post operations in `forum-moderation-post-ops`. Optional `status` SHALL filter by buckets `pending`, `rejected`, or `resolved` (legacy `dismissed` / `acknowledged` roll into `rejected` / `resolved`). List items SHALL expose canonical `status` and SHALL omit `resolutionCode` in the public list DTO. Callers without sufficient forum role SHALL receive HTTP 403.

#### Scenario: Admin lists reports

- **WHEN** a client with effective forum role `admin` requests `GET /api/mod/reports`
- **THEN** the response SHALL be successful
- **AND** items MAY include reports from any board

#### Scenario: Moderator lists only scoped reports

- **WHEN** a client with effective forum role `moderator` and a non-empty `forumBoardIds` list requests `GET /api/mod/reports`
- **THEN** every returned item SHALL have board scope compatible with moderator authorization for that board

#### Scenario: Member cannot list reports

- **WHEN** a client with effective forum role `member` requests `GET /api/mod/reports`
- **THEN** the response SHALL be HTTP 403 with the uniform error envelope

### Requirement: Moderators and admins update report workflow status

The system SHALL expose `PATCH /api/mod/reports/{reportId}` for authenticated moderators and administrators with body `{ "status": "<value>" }` where `<value>` is `pending`, `rejected`, `resolved`, `dismissed` (stored as `rejected`), or `acknowledged` (stored as `resolved`). The server SHALL apply transitions from any prior status consistent with `ModReportsController`. Setting `pending` clears handler fields and `resolutionCode`; `rejected` / `resolved` sets handler and clears `resolutionCode`. Each successful change SHALL persist only the **`forum_reports`** document update and SHALL NOT append moderation audit rows for report status churn (aligned with the repository root **`openspec/specs/forum-report-api`**).

#### Scenario: Moderator updates report in scope

- **WHEN** a moderator calls `PATCH /api/mod/reports/{reportId}` with `{ "status": "rejected" }` for a report inside their managed boards
- **THEN** the response SHALL be successful
- **AND** the stored status SHALL equal `rejected`
- **AND** `handledBySub` SHALL equal the moderator's `sub`

#### Scenario: Moderator forbidden out of scope

- **WHEN** a moderator calls `PATCH /api/mod/reports/{reportId}` for a report outside their managed boards
- **THEN** the response SHALL be HTTP 403 with the uniform error envelope

#### Scenario: Repeat transitions allowed

- **WHEN** an authorized caller changes a report from `resolved` back to `pending` or between `rejected` and `resolved`
- **THEN** the response SHALL succeed when board scope allows
