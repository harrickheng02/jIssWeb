# forum-report-moderation-ui Specification

## Purpose

定义举报入口与举报处理队列的前端可见性、导航与接口调用契约，与用户端 `forum-report-api` 及既有版主 JWT 推导规则对齐。

## ADDED Requirements

### Requirement: Authenticated member can open a report flow from post and reply surfaces

The frontend SHALL expose a report entry point on the forum post detail view and on each reply row (or equivalent reply list item) when the viewer is authenticated. The entry SHALL open a modal, drawer, or dedicated inline form where the user can submit optional `reason` text and SHALL call `POST /api/forum/reports` with the correct `targetType` and `targetId`. Viewers SHALL complete sign-in before the client performs an authenticated report submit.

#### Scenario: Authenticated user reports a post from detail page

- **WHEN** a signed-in member views the post detail page and chooses report on the main post
- **THEN** the client SHALL submit `targetType=post` and `targetId` equal to the post id shown on that page

#### Scenario: Authenticated user reports a reply from the reply list

- **WHEN** a signed-in member chooses report on a reply
- **THEN** the client SHALL submit `targetType=reply` and `targetId` equal to that reply's id

#### Scenario: Reporter sees feedback on success or failure

- **WHEN** the report POST completes
- **THEN** the UI SHALL show explicit success messaging on success and user-visible error messaging on 4xx/5xx responses, including duplicate pending (409) when returned by the API

### Requirement: Moderation shell exposes a report queue reachable from governance

The frontend SHALL expose a control on `/moderation` (governance landing) that navigates to `/moderation/reports` when the effective forum role is moderator or admin, using the same `forumRole` derivation rules as `forum-moderation-sticky-ui`.

#### Scenario: Moderator navigates to report queue

- **WHEN** a user whose effective forum role is moderator or admin opens `/moderation` and uses the report-queue control
- **THEN** the UI SHALL navigate to `/moderation/reports`

#### Scenario: Member does not reach queue directly

- **WHEN** a user whose effective forum role is `member` attempts `/moderation/reports`
- **THEN** routing SHALL redirect per moderation guards (e.g. back to `/moderation`)

### Requirement: Report queue lists items and updates status via PATCH

The report queue view SHALL load data using `GET /api/mod/reports` with optional `status` filter (`pending` | `rejected` | `resolved`). Each item SHALL show target correlation fields, board labeling, timestamps, reporter display, canonical status, and a three-way control mapping to `PATCH /api/mod/reports/{reportId}` with `{ "status": ... }`. Success SHALL refresh list state.

#### Scenario: Moderator loads paged queue

- **WHEN** a moderator or admin opens the report queue view
- **THEN** the client SHALL call `GET /api/mod/reports` with pagination aligned to backend conventions
- **AND** results SHALL render in a readable list layout

#### Scenario: Handler sets status from queue

- **WHEN** a moderator or admin selects pending / rejected / resolved for a report row
- **THEN** the client SHALL call `PATCH /api/mod/reports/{reportId}` with matching `status`

#### Scenario: Queue handles forbidden and unauthorized errors

- **WHEN** the queue request returns HTTP 403 or HTTP 401
- **THEN** the UI SHALL show a permission or sign-in oriented message consistent with other moderation surfaces
