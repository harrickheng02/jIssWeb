## MODIFIED Requirements

### Requirement: Report queue lists items and updates status via PATCH

The report queue view SHALL load data using **`GET /api/mod/reports`** with pagination and optional **`status`** filter aligned with backend buckets **`pending`**, **`rejected`**, and **`resolved`**. Each row SHALL surface board labeling, timestamps, reporter display fields returned by API, canonical **`status`**, and **`handledBySub` / `handledAtUtc`** when relevant. Rows SHALL expose a workflow affordance (`pending`, `rejected`, `resolved`) that maps to **`PATCH /api/mod/reports/{reportId}`** with a JSON body field **`status`** equal to the selected bucket. Applying **`PATCH`** SHALL refresh or reconcile local list state on success.

Hard delete flows SHALL use moderation controls on post detail wired to **`forum-moderation-delete-content`**, independently of report **`PATCH`**.

#### Scenario: Moderator loads paged queue

- **WHEN** a moderator or admin opens **`/moderation/reports`**
- **THEN** the client SHALL call **`GET /api/mod/reports`** with pagination aligned to backend conventions
- **AND** results SHALL render in a readable list layout

#### Scenario: Handler sets status from queue

- **WHEN** a moderator or admin invokes a **`pending`** / **`rejected`** / **`resolved`** control on a report row
- **THEN** the client SHALL call **`PATCH /api/mod/reports/{reportId}`** with **`status`** set accordingly

#### Scenario: Queue handles forbidden and unauthorized errors

- **WHEN** the queue request returns HTTP 403 or HTTP 401
- **THEN** the UI SHALL show a permission or sign-in oriented message consistent with other moderation surfaces

### Requirement: Moderation shell exposes a report queue reachable from governance

The frontend SHALL expose a control on the moderation governance landing (route family **`/moderation`**) that navigates to the report queue **`/moderation/reports`** when the effective forum role is moderator or admin, using the same **`forumRole`** derivation rules as **`forum-moderation-sticky-ui`**.

#### Scenario: Moderator opens report queue from governance

- **WHEN** a user whose effective forum role is moderator or admin opens **`/moderation`** and uses the report-queue entry control
- **THEN** navigation SHALL arrive at **`/moderation/reports`**

#### Scenario: Member gated from queue route

- **WHEN** a user whose effective forum role is **`member`** navigates toward **`/moderation/reports`**
- **THEN** routing SHALL send them to moderated guidance entry points as implemented (for example **`/moderation`**)
