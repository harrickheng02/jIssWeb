## ADDED Requirements

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
