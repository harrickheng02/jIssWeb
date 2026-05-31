## ADDED Requirements

### Requirement: Report queue exposes acknowledge action for pending items

The report queue expanded row SHALL expose an **已受理** (acknowledge) control for reports whose canonical `status` is `pending`. Invoking the control SHALL call `POST /api/mod/reports/{reportId}/acknowledge`. On success the row SHALL reflect `acknowledgedAtUtc` / `acknowledgedBySub` from the API without removing the item from the default pending filter. The control SHALL be disabled or replaced with an already-acknowledged indicator when `acknowledgedAtUtc` is present. Styling SHALL use `forum-tokens.css` variables and primary actions via `el-button type="primary"` where applicable.

#### Scenario: Moderator acknowledges from queue

- **WHEN** a moderator expands a pending report row and clicks acknowledge
- **THEN** the client SHALL POST to `/api/mod/reports/{reportId}/acknowledge`
- **AND** the row SHALL show acknowledged state on success

#### Scenario: Acknowledge button hidden or disabled for closed reports

- **WHEN** a report row has canonical status `resolved` or `rejected`
- **THEN** the acknowledge control SHALL NOT be offered

## MODIFIED Requirements

### Requirement: Report queue lists items and updates status via PATCH

The report queue view SHALL load data using **`GET /api/mod/reports`** with pagination and optional **`status`** filter aligned with backend buckets **`pending`**, **`rejected`**, and **`resolved`**. The UX SHALL **`default`** to listing **`pending`** items (explicit filter or **`status=pending`**); **`全部`** / **all statuses** aligns with **`status`** omitted on the **`GET`** request. Each row SHALL surface board labeling, timestamps, reporter display fields returned by API, canonical **`status`**, **`handledBySub` / `handledAtUtc`** when relevant, and **`acknowledgedAtUtc` / `acknowledgedBySub`** when set. Rows SHALL expose a workflow affordance (`pending`, `rejected`, `resolved`) that maps to **`PATCH /api/mod/reports/{reportId}`** with **`{ "status": "<bucket>" }`** where **`bucket`** matches the backend contract aliases above (`dismissed` / `acknowledged` acceptable where frontend maps them). Applying **`PATCH`** SHALL refresh or reconcile local list state on success.

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
- **THEN** the UI SHALL show an explicit error or redirect consistent with other moderation routes
