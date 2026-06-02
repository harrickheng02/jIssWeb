## Purpose

定义论坛标签管理前端规格：版主和管理员均可访问的标签管理视图，包含路由守卫、导航入口、标签列表、新建、禁用/启用与删除操作，以及发帖时的标签输入混合模式。

## Requirements

### Requirement: canModerate gating for tag admin UI

All tag admin UI features SHALL gate on `auth.canModerate` (moderator or admin). The auth store SHALL expose `canModerate` as the access predicate; no separate `canAdmin` property is required for these features.

#### Scenario: Moderator user has canModerate true

- **WHEN** the JWT payload contains `forumRole == "moderator"` or `forumRole == "admin"`
- **THEN** `auth.canModerate` SHALL be `true` and the tag admin navigation entry SHALL be visible

#### Scenario: Non-moderator user cannot access tag admin

- **WHEN** the JWT payload does not contain `forumRole == "moderator"` or `forumRole == "admin"`
- **THEN** `auth.canModerate` SHALL be `false` and the tag admin route SHALL be inaccessible

### Requirement: Admin tags route with requiresModerate guard

The Vue Router SHALL register a route `/admin/tags` with meta flag `requiresModerate: true`. The route guard SHALL redirect unauthenticated users to the login page and authenticated non-moderator/admin users to the home page.

#### Scenario: Moderator or admin accesses /admin/tags

- **WHEN** a user with `canModerate === true` navigates to `/admin/tags`
- **THEN** the `AdminTagsView` component SHALL render

#### Scenario: Non-moderator redirected from /admin/tags

- **WHEN** a user with `canModerate === false` attempts to navigate to `/admin/tags`
- **THEN** the router SHALL redirect to the homepage

#### Scenario: Unauthenticated redirected to login

- **WHEN** an unauthenticated user navigates to `/admin/tags`
- **THEN** the router SHALL redirect to the login page

### Requirement: Admin tags navigation entry in moderation page

The tag admin entry SHALL be accessible from the governance/moderation page (`/moderation`), visible only when `canModerate === true`, linking to `/admin/tags`.

#### Scenario: Moderator or admin sees tags management link

- **WHEN** a user with `canModerate === true` visits the moderation page
- **THEN** a "标签管理" navigation entry SHALL be visible and SHALL navigate to `/admin/tags`

#### Scenario: Non-moderator does not see tags management link

- **WHEN** a user with `canModerate === false` visits the moderation page
- **THEN** no "标签管理" entry SHALL be present

### Requirement: AdminTagsView displays tag list with status filter

`AdminTagsView` SHALL display a paginated table of forum tags with columns: 名称, 状态徽标, 使用数, 操作。A filter bar SHALL allow filtering by Status (`全部` / `active` / `disabled`). The view SHALL use forum-tokens.css CSS variables exclusively for colors and spacing (8px grid). There is no "合并目标" column and no merge action button in the operations column.

#### Scenario: Table loaded on mount

- **WHEN** a moderator or admin navigates to `/admin/tags`
- **THEN** the table SHALL fetch `GET /api/forum/admin/tags` and display rows, with a loading state while fetching

#### Scenario: Status filter applied

- **WHEN** a moderator or admin selects "disabled" from the status filter
- **THEN** the table SHALL re-fetch with `?status=disabled` and display only disabled tags

#### Scenario: Search by name

- **WHEN** a moderator or admin types in the search box
- **THEN** the table SHALL re-fetch with `?q=<search>` after a debounce period and display matching tags

### Requirement: AdminTagsView create tag dialog

`AdminTagsView` SHALL include a "新建标签" button (`el-button type="primary"`) that opens a dialog with a Name field (required, max 32 chars) and an optional Description field. On submit, the dialog SHALL call `POST /api/forum/admin/tags` and refresh the table.

#### Scenario: Successful creation refreshes table

- **WHEN** a moderator or admin fills in a valid name and clicks the confirm button
- **THEN** the dialog SHALL close, the table SHALL refresh, and a success toast SHALL appear

#### Scenario: Duplicate slug shows error

- **WHEN** the API returns `TAG_SLUG_CONFLICT`
- **THEN** the dialog SHALL remain open and display an error message inline near the Name field

### Requirement: AdminTagsView disable and enable actions

Each `active` tag row SHALL have a "禁用" action button. Each `disabled` tag row SHALL have a "启用" action button. Clicking either SHALL call the corresponding endpoint and refresh the row.

#### Scenario: Disable active tag

- **WHEN** a moderator or admin clicks "禁用" on an active tag
- **THEN** a confirmation dialog SHALL appear; on confirm, `POST /api/forum/admin/tags/{id}/disable` SHALL be called and the row status SHALL update to `disabled`

#### Scenario: Enable disabled tag

- **WHEN** a moderator or admin clicks "启用" on a disabled tag
- **THEN** `POST /api/forum/admin/tags/{id}/enable` SHALL be called without a confirmation dialog, and the row status SHALL update to `active`

### Requirement: AdminTagsView delete action

Each tag row SHALL have a "删除" action button regardless of the tag's current status or UseCount. Clicking it SHALL call `DELETE /api/forum/admin/tags/{id}` after confirmation and refresh the table.

#### Scenario: Delete tag with confirmation

- **WHEN** a moderator or admin clicks "删除" on any tag row
- **THEN** a confirmation dialog SHALL appear; on confirm, `DELETE /api/forum/admin/tags/{id}` SHALL be called and the row SHALL be removed from the table

#### Scenario: Delete non-existent tag shows error

- **WHEN** the API returns 404
- **THEN** the view SHALL display an error toast and refresh the table

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
