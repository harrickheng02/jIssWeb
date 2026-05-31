# forum-report-moderation-ui Specification

## Purpose

定义举报入口与举报处理队列的前端可见性、导航与接口调用契约，与用户端 **`forum-report-api`**、**`forum-moderation-delete-content`**（帖子详情与举报队列展开区内共用 **`DELETE`** 删帖 / 删回复能力）以及既有版主 JWT 推导规则对齐。

## Requirements

### Requirement: Authenticated member can open a report flow from post and reply surfaces

The frontend SHALL expose a report entry point on the forum post detail view and on each reply row (or equivalent reply list item) when the viewer is authenticated. The entry SHALL open a modal, drawer, or dedicated inline form where the user can submit optional `reason` text and SHALL call `POST /api/forum/reports` with the correct `targetType` and `targetId`. Anonymous viewers SHALL navigate through sign-in before submitting a report when the UX requires authentication.

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

The frontend SHALL expose a control on the moderation governance landing (route family **`/moderation`**) that navigates to the report queue **`/moderation/reports`** when the effective forum role is moderator or admin, using the same **`forumRole`** derivation rules as **`forum-moderation-sticky-ui`**. Unauthorized visitors SHALL be routed away from the queue page per router guards consistent with other moderation-only routes.

#### Scenario: Moderator opens report queue from governance

- **WHEN** a user whose effective forum role is moderator or admin opens **`/moderation`** and uses the report-queue entry control
- **THEN** navigation SHALL arrive at **`/moderation/reports`**

#### Scenario: Member gated from queue route

- **WHEN** a user whose effective forum role is **`member`** navigates toward **`/moderation/reports`**
- **THEN** routing SHALL send them to moderated guidance entry points as implemented (for example **`/moderation`**)

### Requirement: Report queue lists items and updates status via PATCH

The report queue view SHALL load data using **`GET /api/mod/reports`** with pagination and optional **`status`** filter aligned with backend buckets **`pending`**, **`rejected`**, and **`resolved`**. The UX SHALL **`default`** to listing **`pending`** items (explicit filter or **`status=pending`**); **`全部`** / **all statuses** aligns with **`status`** omitted on the **`GET`** request. Each row SHALL surface board labeling, timestamps, reporter display fields returned by API, canonical **`status`**, and **`handledBySub` / `handledAtUtc`** when relevant. Rows SHALL expose a workflow affordance (`pending`, `rejected`, `resolved`) that maps to **`PATCH /api/mod/reports/{reportId}`** with **`{ "status": "<bucket>" }`** where **`bucket`** matches the backend contract aliases above (`dismissed` / `acknowledged` acceptable where frontend maps them). Applying **`PATCH`** SHALL refresh or reconcile local list state on success.

Content removal (delete post, delete reply) SHALL call **`forum-moderation-delete-content`** **`DELETE`** endpoints from moderation surfaces: **post detail** and **report queue** when an expanded row exposes the same controls (shared component or equivalent), separately from **`PATCH`** on reports.

#### Scenario: Moderator loads paged queue

- **WHEN** a moderator or admin opens **`/moderation/reports`**
- **THEN** the client SHALL call **`GET /api/mod/reports`** with pagination aligned to backend conventions and **`status=pending`** by default unless the moderator selects **全部状态** (**`status`** omitted for all)
- **AND** results SHALL render in a readable list layout
#### Scenario: Handler sets status from queue

- **WHEN** a moderator or admin invokes a **`pending`** / **`rejected`** / **`resolved`** control on a report row
- **THEN** the client SHALL call **`PATCH /api/mod/reports/{reportId}`** with **`status`** set accordingly

#### Scenario: Queue handles forbidden and unauthorized errors

- **WHEN** the queue request returns HTTP 403 or HTTP 401
- **THEN** the UI SHALL show a permission or sign-in oriented message consistent with other moderation surfaces

### Requirement: Report queue exposes warning and mute controls with required reason

The expanded report-queue row (or equivalent governance panel on `/moderation/reports`) SHALL expose controls to issue a **warning** or **mute** against the reported content author. The mute control SHALL offer preset durations **24 hours (default selected)**, **7 days**, and **30 days**. A **reason** text field SHALL be required before submit; the submit affordance SHALL remain disabled until `reason` is non-empty after trim. Successful calls SHALL use `POST /api/mod/users/{sub}/sanctions` or `POST /api/mod/reports/{reportId}/sanctions` with `reportId` set to the current queue item, `type`, `durationPreset` when muting, and `reason`. The UI SHALL surface `403 FORUM_MUTED` feedback only on the author's own compose surfaces (not on the moderation panel).

#### Scenario: Default mute duration is twenty-four hours

- **WHEN** a moderator opens the mute dialog from a report row
- **THEN** the duration selector SHALL default to twenty-four hours

#### Scenario: Submit disabled without reason

- **WHEN** the reason field is empty
- **THEN** warn and mute submit buttons SHALL be disabled

#### Scenario: Delete from queue sends reportId only

- **WHEN** a moderator deletes post or reply content from the expanded report row
- **THEN** the client SHALL call the moderation delete endpoint with `reportId` in the request body
- **AND** the client SHALL NOT prompt for a delete reason or notify the content author

### Requirement: Muted member sees compose blocking feedback

When a muted member attempts a blocked write from post compose or reply UI, the frontend SHALL display user-visible messaging derived from `FORUM_MUTED`, including localized `mutedUntilUtc` when returned by the API.

#### Scenario: Compose shows mute message

- **WHEN** a muted user submits a post and receives `403` with code `FORUM_MUTED`
- **THEN** the UI SHALL show that posting is restricted until the indicated time
