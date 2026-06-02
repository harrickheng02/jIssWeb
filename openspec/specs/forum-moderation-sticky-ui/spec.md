# forum-moderation-sticky-ui Specification

## Purpose

版主帖子详情置顶/操作记录等前端行为与后端 `POST /api/mod/posts/{postId}/sticky` 等对齐。手工验收步骤见归档：`openspec/changes/archive/2026-04-20-frontend-moderation-sticky-ui-minset/manual-qa.md`。
## Requirements
### Requirement: Frontend derives effective forum role from JWT
The frontend SHALL derive an effective forum role from the access token claim `forumRole` for use in UI gating of moderation controls.

#### Scenario: Token with moderator role enables moderation UI
- **WHEN** a user has a valid access token whose `forumRole` claim equals `moderator`
- **THEN** the UI SHALL treat the user as a moderator for the purposes of showing moderation controls

#### Scenario: Token with admin role enables moderation UI
- **WHEN** a user has a valid access token whose `forumRole` claim equals `admin`
- **THEN** the UI SHALL treat the user as an admin for the purposes of showing moderation controls

#### Scenario: Missing forumRole behaves as member
- **WHEN** a user has a valid access token that omits `forumRole`
- **THEN** the UI SHALL treat the user as a member for the purposes of showing moderation controls

### Requirement: Post detail exposes sticky moderation controls to moderators and admins
The frontend SHALL expose sticky moderation actions on the post detail view for moderators and admins, and SHALL call the backend moderation endpoint to perform the action.

#### Scenario: Moderator toggles sticky on a post
- **WHEN** a user whose effective forum role is moderator or admin views a post detail page
- **THEN** the UI SHALL provide a control to set sticky when the current post is not sticky
- **AND** the UI SHALL provide a control to unset sticky when the current post is sticky

#### Scenario: Successful set sticky updates UI state
- **WHEN** a moderator/admin triggers the set-sticky action
- **THEN** the client SHALL call `POST /api/mod/posts/{postId}/sticky` with JSON body containing `isSticky=true`
- **AND** on success the UI SHALL update to reflect `isSticky=true` for the post

#### Scenario: Successful unset sticky updates UI state
- **WHEN** a moderator/admin triggers the unset-sticky action
- **THEN** the client SHALL call `POST /api/mod/posts/{postId}/sticky` with JSON body containing `isSticky=false`
- **AND** on success the UI SHALL update to reflect `isSticky=false` for the post

#### Scenario: Member cannot see moderation controls
- **WHEN** a user whose effective forum role is member views a post detail page
- **THEN** the UI SHALL NOT render the sticky moderation controls

### Requirement: Sticky moderation actions handle common error outcomes
The frontend SHALL provide user-visible feedback for moderation endpoint failures.

#### Scenario: Unauthenticated moderation request leads to auth flow
- **WHEN** a moderation request returns 401
- **THEN** the client SHALL follow the existing authentication recovery path defined by the frontend shell (refresh or re-login flow)

#### Scenario: Forbidden moderation request shows permission error
- **WHEN** a moderation request returns 403
- **THEN** the UI SHALL present a permission error message that indicates the user lacks authorization for that post

#### Scenario: Missing post shows not-found
- **WHEN** a moderation request returns 404
- **THEN** the UI SHALL present a not-found message for the target post

### Requirement: Post detail exposes moderation audit history panel
The frontend SHALL provide a panel on the post detail view that loads and displays moderation audit items for the post. The panel SHALL allow moderators and admins to filter audit items by moderation action type and by an occurred-at time range, and SHALL paginate results when totalCount exceeds pageSize. Changing filters SHALL reset pagination to page 1 and re-fetch from the backend with matching query parameters.

#### Scenario: Authorized user opens audit panel
- **WHEN** a moderator/admin opens the audit history panel for a post
- **THEN** the client SHALL call `GET /api/mod/audit?targetType=post&targetId={postId}` with pagination parameters
- **AND** the UI SHALL render each returned item including a user-facing action label, operator display name, and occurred-at timestamp

#### Scenario: User applies action filter
- **WHEN** a moderator selects a specific action type filter in the audit panel
- **THEN** the client SHALL include the corresponding `action` query parameter on the audit request
- **AND** the UI SHALL display only items returned by the filtered response

#### Scenario: User applies time range filter
- **WHEN** a moderator selects a start and end datetime in the audit panel
- **THEN** the client SHALL send `fromUtc` and `toUtc` query parameters in ISO-8601 form
- **AND** the UI SHALL reflect the filtered result set

#### Scenario: Pagination loads next page
- **WHEN** totalCount exceeds pageSize and the user navigates to page 2
- **THEN** the client SHALL request the audit endpoint with `page=2`
- **AND** the UI SHALL replace the list with the second page of items

### Requirement: Audit panel communicates empty and error states
The audit panel SHALL present a clear empty state when the filtered query returns zero items, and SHALL present a user-visible error when the audit request fails, without clearing unrelated governance controls on the post detail view.

#### Scenario: Filtered query returns no rows
- **WHEN** the audit API returns success with zero items for the current filters
- **THEN** the UI SHALL show an empty-state message indicating no matching records

#### Scenario: Audit request fails
- **WHEN** the audit API returns a non-success envelope or network error
- **THEN** the UI SHALL show an error message and SHALL allow retry

### Requirement: Moderation hub exposes unified tab navigation

The frontend SHALL provide a shared moderation layout at `/moderation` with a tab strip for **审计动态** (`/moderation/audit`), **举报队列** (`/moderation/reports`), and **标签管理** (`/moderation/tags`). Navigating to `/moderation` SHALL redirect to `/moderation/audit` as the default tab. The legacy path `/admin/tags` SHALL redirect to `/moderation/tags`. All moderation child routes SHALL require authentication and `requiresModerate`. The shell **治理** entry SHALL navigate directly to `/moderation/audit`. Styling SHALL use design tokens from `forum-tokens.css` only. A standalone governance guide landing page SHALL NOT be the primary entry.

#### Scenario: Moderator opens governance from header

- **WHEN** a moderator selects **治理** in the user menu
- **THEN** the client SHALL navigate to `/moderation/audit`
- **AND** the **审计动态** tab SHALL appear selected

#### Scenario: Moderator switches tabs within the hub

- **WHEN** a moderator on `/moderation/reports` selects **标签管理**
- **THEN** the client SHALL navigate to `/moderation/tags`
- **AND** the tab strip SHALL remain visible on the shared layout

#### Scenario: Member cannot open moderation routes

- **WHEN** a member navigates to `/moderation/audit`
- **THEN** the existing moderation route guard SHALL block or redirect per shell policy

### Requirement: Global audit feed page loads and filters feed API

The frontend SHALL implement `/moderation/audit` that calls `GET /api/mod/audit/feed` via the shared model API client (`createClient`). The page SHALL provide filters for occurred-at time range, moderation action type (aligned with known action codes used in the post detail audit panel), and board. For administrators the board control SHALL allow site-wide listing when no board is selected. For moderators the default board filter SHALL represent all boards in `forumBoardIds` without requiring a manual board pick; the UI SHALL provide a control to clear a single-board narrow filter and return to all visible boards (still within JWT scope). The page SHALL paginate results and SHALL present empty and error states. Row presentation SHALL include action label, operator display name, timestamp, board label, and deep links to post detail when `postId` is present.

#### Scenario: Moderator opens audit feed with default scope

- **WHEN** a moderator opens `/moderation/audit`
- **THEN** the client SHALL request the feed without `boardId` unless the user previously narrowed the filter
- **AND** the list SHALL show items returned for their authorized boards

#### Scenario: User applies filters and pagination

- **WHEN** the user changes action or time filters
- **THEN** the client SHALL reset to page 1 and re-fetch with matching query parameters
- **WHEN** the user moves to page 2
- **THEN** the client SHALL request `page=2` with unchanged filters

### Requirement: Audit feed page exports CSV

The audit feed page SHALL provide a primary action to download CSV using `GET /api/mod/audit/export` with the same query parameters as the current feed filters. On `EXPORT_TOO_LARGE` the UI SHALL show a user-visible message indicating the result set exceeds the export limit. On success the browser SHALL download the CSV file.

#### Scenario: Export uses current filters

- **WHEN** a moderator clicks export on the audit feed page with filters applied
- **THEN** the client SHALL call the export endpoint with the same `action`, `fromUtc`, `toUtc`, and `boardId` parameters as the list request

#### Scenario: Export too large shows error

- **WHEN** the export endpoint returns `EXPORT_TOO_LARGE`
- **THEN** the UI SHALL show an error message and SHALL NOT claim success

### Requirement: Legacy admin tags path redirects

The frontend SHALL register `/admin/tags` as a redirect to `/moderation/tags` so existing bookmarks continue to work.

#### Scenario: Bookmark to admin tags opens moderation tags tab

- **WHEN** a moderator navigates to `/admin/tags`
- **THEN** the client SHALL redirect to `/moderation/tags` with the **标签管理** tab selected

