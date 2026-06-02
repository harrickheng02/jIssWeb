## Purpose

定义论坛标签管理 HTTP 契约：面向版主和管理员的标签 CRUD 操作，包括创建、编辑、禁用/启用、删除及开发环境数据种子接口。所有接口均需 JWT `forumRole == "moderator"` 或 `forumRole == "admin"`。

## Requirements

### Requirement: Admin tag list endpoint

The system SHALL expose `GET /api/forum/admin/tags` exclusively to users with JWT `forumRole == "moderator"` or `forumRole == "admin"`. The endpoint SHALL return a paginated list of `ForumTagRecord` summaries supporting optional filtering by `status` (`active` | `disabled`) and keyword search on `Name` or `Slug`.

#### Scenario: Moderator or admin retrieves full tag list

- **WHEN** a moderator or admin sends `GET /api/forum/admin/tags` with valid pagination
- **THEN** the response SHALL include all tags (active and disabled statuses) ordered by `UseCount` descending, with fields: `id`, `name`, `slug`, `description`, `status`, `useCount`, `createdAtUtc`, `updatedAtUtc`

#### Scenario: Filter by status

- **WHEN** a moderator or admin sends `GET /api/forum/admin/tags?status=disabled`
- **THEN** only tags with Status `disabled` SHALL be returned

#### Scenario: Keyword search on name

- **WHEN** a moderator or admin sends `GET /api/forum/admin/tags?q=AI`
- **THEN** only tags whose Name or Slug contains the query (case-insensitive) SHALL be returned

#### Scenario: Non-moderator/admin rejected

- **WHEN** a request to `GET /api/forum/admin/tags` is made without JWT `forumRole == "moderator"` or `forumRole == "admin"`
- **THEN** the response SHALL be 403

### Requirement: Admin create tag endpoint

The system SHALL expose `POST /api/forum/admin/tags` to moderators and admins, creating a new tag record with initial Status `active` and `UseCount` 0.

#### Scenario: Valid tag created

- **WHEN** a moderator or admin posts `{ "name": "人工智能", "description": "..." }` (description optional)
- **THEN** the system SHALL normalize `Slug` as lowercase trimmed `Name`, persist the record with Status `active`, UseCount 0, CreatedBySub from JWT `sub`, and return the created tag including its `id`

#### Scenario: Duplicate slug rejected

- **WHEN** a moderator or admin creates a tag whose normalized Slug already exists
- **THEN** the response SHALL be 409 with error code `TAG_SLUG_CONFLICT`

#### Scenario: Empty or whitespace name rejected

- **WHEN** a moderator or admin submits a `name` that is empty or whitespace-only after trimming
- **THEN** the response SHALL be 400 with error code `INVALID_TAG_NAME`

#### Scenario: Name exceeding max length rejected

- **WHEN** a moderator or admin submits a `name` whose trimmed length exceeds 32 characters
- **THEN** the response SHALL be 400 with error code `INVALID_TAG_NAME`

### Requirement: Admin edit tag endpoint

The system SHALL expose `PATCH /api/forum/admin/tags/{id}` to moderators and admins, allowing update of `Name` and/or `Description`.

#### Scenario: Name updated

- **WHEN** a moderator or admin patches `{ "name": "新名称" }` for an active or disabled tag
- **THEN** the tag's `Name` SHALL be updated and `Slug` SHALL be re-derived from the new Name; `UpdatedAtUtc` and `UpdatedBySub` SHALL be set

#### Scenario: Renaming to existing slug rejected

- **WHEN** a moderator or admin renames a tag and the new Slug conflicts with another existing tag
- **THEN** the response SHALL be 409 with error code `TAG_SLUG_CONFLICT`

### Requirement: Admin disable tag endpoint

The system SHALL expose `POST /api/forum/admin/tags/{id}/disable` to moderators and admins, transitioning a tag from `active` to `disabled`.

#### Scenario: Active tag disabled

- **WHEN** a moderator or admin disables an active tag
- **THEN** the tag's Status SHALL be `disabled` and the tag SHALL no longer appear in `GET /api/forum/tags/popular` or `GET /api/forum/tags/suggest`

#### Scenario: Already disabled tag — idempotent

- **WHEN** a moderator or admin disables a tag that is already `disabled`
- **THEN** the response SHALL be 200 with no change (idempotent)

### Requirement: Admin enable tag endpoint

The system SHALL expose `POST /api/forum/admin/tags/{id}/enable` to moderators and admins, transitioning a tag from `disabled` to `active`.

#### Scenario: Disabled tag enabled

- **WHEN** a moderator or admin enables a disabled tag
- **THEN** the tag's Status SHALL be `active` and the tag SHALL appear again in public endpoints

#### Scenario: Already active tag — idempotent

- **WHEN** a moderator or admin enables a tag that is already `active`
- **THEN** the response SHALL be 200 with no change (idempotent)

### Requirement: Admin delete tag endpoint

The system SHALL expose `DELETE /api/forum/admin/tags/{id}` to moderators and admins. Deletion SHALL be permitted for any existing tag regardless of UseCount or Status.

#### Scenario: Tag deleted

- **WHEN** a moderator or admin sends `DELETE /api/forum/admin/tags/{id}` for an existing tag
- **THEN** the record SHALL be permanently removed from `forum_tags`

#### Scenario: Non-existent tag deletion rejected

- **WHEN** a moderator or admin attempts to delete a tag id that does not exist
- **THEN** the response SHALL be 404

### Requirement: Development seed endpoint

In non-production environments, the system SHALL expose `POST /api/forum/admin/tags/seed-from-posts` to moderators and admins, which aggregates all distinct tags from existing `forum_posts` and upserts them into `forum_tags` with Status `active` and computed UseCount. This endpoint SHALL NOT be registered when `ASPNETCORE_ENVIRONMENT` is `Production`.

#### Scenario: Seed creates tags from posts

- **WHEN** a moderator or admin calls the seed endpoint in a development environment
- **THEN** all distinct tag strings from forum_posts are upserted to forum_tags (existing tags are not overwritten), and UseCount is calculated from actual post data

#### Scenario: Seed unavailable in production

- **WHEN** the application runs with `ASPNETCORE_ENVIRONMENT=Production`
- **THEN** `POST /api/forum/admin/tags/seed-from-posts` SHALL return 404 (route not registered)
