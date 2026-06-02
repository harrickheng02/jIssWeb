## ADDED Requirements

### Requirement: canAdmin derived in auth store

The frontend auth store SHALL expose a computed property `canAdmin` derived as `forumRole.value === 'admin'`, aligned with the backend `RequireForumAdmin` attribute. All admin-only UI features SHALL gate on `canAdmin`.

#### Scenario: Admin user has canAdmin true

- **WHEN** the JWT payload contains `forumRole == "admin"`
- **THEN** `auth.canAdmin` SHALL be `true` and admin navigation items SHALL be visible

#### Scenario: Moderator user has canAdmin false

- **WHEN** the JWT payload contains `forumRole == "moderator"`
- **THEN** `auth.canAdmin` SHALL be `false` and the admin tags route SHALL be inaccessible

### Requirement: Admin tags route with requiresAdmin guard

The Vue Router SHALL register a route `/admin/tags` with meta flag `requiresAdmin: true`. The route guard SHALL redirect unauthenticated users to the login page and authenticated non-admin users to the home page.

#### Scenario: Admin accesses /admin/tags

- **WHEN** a user with `canAdmin === true` navigates to `/admin/tags`
- **THEN** the `AdminTagsView` component SHALL render

#### Scenario: Non-admin redirected from /admin/tags

- **WHEN** a user with `canAdmin === false` attempts to navigate to `/admin/tags`
- **THEN** the router SHALL redirect to the homepage

#### Scenario: Unauthenticated redirected to login

- **WHEN** an unauthenticated user navigates to `/admin/tags`
- **THEN** the router SHALL redirect to the login page

### Requirement: Admin tags navigation entry

The header user menu SHALL display an "标签管理" menu item visible only when `canAdmin === true`, linking to `/admin/tags`.

#### Scenario: Admin sees tags management link

- **WHEN** a user with `canAdmin === true` opens the header user menu
- **THEN** an "标签管理" item SHALL be visible and SHALL navigate to `/admin/tags`

#### Scenario: Non-admin does not see tags management link

- **WHEN** a user with `canAdmin === false` opens the header user menu
- **THEN** no "标签管理" item SHALL be present

### Requirement: AdminTagsView displays tag list with status filter

`AdminTagsView` SHALL display a paginated table of forum tags with columns: 名称, 状态徽标, 使用数, 操作。A filter bar SHALL allow filtering by Status (`全部` / `active` / `disabled` / `merged`). The view SHALL use forum-tokens.css CSS variables exclusively for colors and spacing (8px grid).

#### Scenario: Table loaded on mount

- **WHEN** an admin navigates to `/admin/tags`
- **THEN** the table SHALL fetch `GET /api/forum/admin/tags` and display rows, with a loading state while fetching

#### Scenario: Status filter applied

- **WHEN** an admin selects "disabled" from the status filter
- **THEN** the table SHALL re-fetch with `?status=disabled` and display only disabled tags

#### Scenario: Search by name

- **WHEN** an admin types in the search box
- **THEN** the table SHALL re-fetch with `?q=<search>` after a debounce period and display matching tags

### Requirement: AdminTagsView create tag dialog

`AdminTagsView` SHALL include a "新建标签" button (`el-button type="primary"`) that opens a dialog with a Name field (required, max 32 chars) and an optional Description field. On submit, the dialog SHALL call `POST /api/forum/admin/tags` and refresh the table.

#### Scenario: Successful creation refreshes table

- **WHEN** an admin fills in a valid name and clicks the confirm button
- **THEN** the dialog SHALL close, the table SHALL refresh, and a success toast SHALL appear

#### Scenario: Duplicate slug shows error

- **WHEN** the API returns `TAG_SLUG_CONFLICT`
- **THEN** the dialog SHALL remain open and display an error message inline near the Name field

### Requirement: AdminTagsView disable and enable actions

Each `active` tag row SHALL have a "禁用" action button. Each `disabled` tag row SHALL have a "启用" action button. `merged` rows SHALL show neither action. Clicking either SHALL call the corresponding endpoint and refresh the row.

#### Scenario: Disable active tag

- **WHEN** an admin clicks "禁用" on an active tag
- **THEN** a confirmation dialog SHALL appear; on confirm, `POST /api/forum/admin/tags/{id}/disable` SHALL be called and the row status SHALL update to `disabled`

#### Scenario: Enable disabled tag

- **WHEN** an admin clicks "启用" on a disabled tag
- **THEN** `POST /api/forum/admin/tags/{id}/enable` SHALL be called without a confirmation dialog, and the row status SHALL update to `active`

### Requirement: AdminTagsView merge tag dialog

Each `active` tag row SHALL have a "合并" action. Clicking it SHALL open a dialog with a target tag search input (autocomplete from `GET /api/forum/admin/tags?q=...&status=active`). The dialog SHALL show a warning that the operation is irreversible. On confirm, `POST /api/forum/admin/tags/{id}/merge` SHALL be called.

#### Scenario: Merge dialog shows warning

- **WHEN** an admin opens the merge dialog
- **THEN** a warning message SHALL be displayed stating the operation is irreversible and will update all posts

#### Scenario: Successful merge updates row

- **WHEN** an admin selects a valid target tag and confirms the merge
- **THEN** the source tag's row SHALL update status to `merged` and show `MergedIntoSlug`

#### Scenario: Cannot select self as merge target

- **WHEN** an admin opens the merge dialog for tag T
- **THEN** tag T itself SHALL not appear in the target autocomplete results

### Requirement: Post compose tag input uses suggest API with free-form creation (hybrid mode)

The post compose component's tag input field SHALL call `GET /api/forum/tags/suggest?q=<input>&limit=10` for autocomplete suggestions as the user types. Users MAY select a suggested (registered) tag OR type a new tag string not present in the registry. Both modes are permitted (hybrid mode).

#### Scenario: Tag suggestions appear while typing

- **WHEN** a user types in the tag input during post composition
- **THEN** autocomplete suggestions SHALL appear sourced from the suggest API (active registered tags only)

#### Scenario: Free-form tag can be submitted

- **WHEN** a user types a string that returns no suggestions and presses Enter or selects the typed value
- **THEN** the tag SHALL be accepted and added to the selection (free-form creation allowed)

#### Scenario: Max tag count enforced client-side

- **WHEN** a user attempts to add an 11th tag
- **THEN** the input SHALL reject it with a warning (max 10 tags)
