## ADDED Requirements

### Requirement: Admin tag list endpoint

The system SHALL expose `GET /api/forum/admin/tags` exclusively to users with JWT `forumRole == "admin"`. The endpoint SHALL return a paginated list of `ForumTagRecord` summaries supporting optional filtering by `status` (`active` | `disabled` | `merged`) and keyword search on `Name` or `Slug`.

#### Scenario: Admin retrieves full tag list

- **WHEN** an admin sends `GET /api/forum/admin/tags` with valid pagination
- **THEN** the response SHALL include all tags (all statuses) ordered by `UseCount` descending, with fields: `id`, `name`, `slug`, `description`, `status`, `mergedIntoSlug`, `useCount`, `createdAtUtc`, `updatedAtUtc`

#### Scenario: Filter by status

- **WHEN** an admin sends `GET /api/forum/admin/tags?status=disabled`
- **THEN** only tags with Status `disabled` SHALL be returned

#### Scenario: Keyword search on name

- **WHEN** an admin sends `GET /api/forum/admin/tags?q=AI`
- **THEN** only tags whose Name or Slug contains the query (case-insensitive) SHALL be returned

#### Scenario: Non-admin rejected

- **WHEN** a request to `GET /api/forum/admin/tags` is made without JWT `forumRole == "admin"`
- **THEN** the response SHALL be 403

### Requirement: Admin create tag endpoint

The system SHALL expose `POST /api/forum/admin/tags` exclusively to admins, creating a new tag record with initial Status `active` and `UseCount` 0.

#### Scenario: Valid tag created

- **WHEN** an admin posts `{ "name": "人工智能", "description": "..." }` (description optional)
- **THEN** the system SHALL normalize `Slug` as lowercase trimmed `Name`, persist the record with Status `active`, UseCount 0, CreatedBySub from JWT `sub`, and return the created tag including its `id`

#### Scenario: Duplicate slug rejected

- **WHEN** an admin creates a tag whose normalized Slug already exists
- **THEN** the response SHALL be 409 with error code `TAG_SLUG_CONFLICT`

#### Scenario: Empty or whitespace name rejected

- **WHEN** an admin submits a `name` that is empty or whitespace-only after trimming
- **THEN** the response SHALL be 400 with error code `INVALID_TAG_NAME`

#### Scenario: Name exceeding max length rejected

- **WHEN** an admin submits a `name` whose trimmed length exceeds 32 characters
- **THEN** the response SHALL be 400 with error code `INVALID_TAG_NAME`

### Requirement: Admin edit tag endpoint

The system SHALL expose `PATCH /api/forum/admin/tags/{id}` exclusively to admins, allowing update of `Name` and/or `Description`. Editing a merged tag's Name SHALL be rejected.

#### Scenario: Name updated

- **WHEN** an admin patches `{ "name": "新名称" }` for an active or disabled tag
- **THEN** the tag's `Name` SHALL be updated and `Slug` SHALL be re-derived from the new Name; `UpdatedAtUtc` and `UpdatedBySub` SHALL be set

#### Scenario: Renaming to existing slug rejected

- **WHEN** an admin renames a tag and the new Slug conflicts with another existing tag
- **THEN** the response SHALL be 409 with error code `TAG_SLUG_CONFLICT`

#### Scenario: Editing merged tag rejected

- **WHEN** an admin attempts to edit a tag with Status `merged`
- **THEN** the response SHALL be 409 with error code `TAG_ALREADY_MERGED`

### Requirement: Admin disable tag endpoint

The system SHALL expose `POST /api/forum/admin/tags/{id}/disable` exclusively to admins, transitioning a tag from `active` to `disabled`.

#### Scenario: Active tag disabled

- **WHEN** an admin disables an active tag
- **THEN** the tag's Status SHALL be `disabled` and the tag SHALL no longer appear in `GET /api/forum/tags/popular` or `GET /api/forum/tags/suggest`

#### Scenario: Already disabled tag — idempotent

- **WHEN** an admin disables a tag that is already `disabled`
- **THEN** the response SHALL be 200 with no change (idempotent)

#### Scenario: Merged tag cannot be disabled

- **WHEN** an admin attempts to disable a `merged` tag
- **THEN** the response SHALL be 409 with error code `TAG_ALREADY_MERGED`

### Requirement: Admin enable tag endpoint

The system SHALL expose `POST /api/forum/admin/tags/{id}/enable` exclusively to admins, transitioning a tag from `disabled` to `active`.

#### Scenario: Disabled tag enabled

- **WHEN** an admin enables a disabled tag
- **THEN** the tag's Status SHALL be `active` and the tag SHALL appear again in public endpoints

#### Scenario: Already active tag — idempotent

- **WHEN** an admin enables a tag that is already `active`
- **THEN** the response SHALL be 200 with no change (idempotent)

### Requirement: Admin merge tag endpoint

The system SHALL expose `POST /api/forum/admin/tags/{id}/merge` exclusively to admins. The request body SHALL include `targetSlug` identifying the target tag. The operation SHALL: (1) validate source is `active` and target is `active`; (2) update all posts synchronously; (3) mark source as `merged`; (4) recalculate target's UseCount.

#### Scenario: Valid merge completes synchronously

- **WHEN** an admin merges source tag S (active) into target tag T (active) where S ≠ T
- **THEN** all forum_posts containing S.Name in Tags SHALL have S.Name replaced by T.Name; S.Status SHALL be `merged`; S.MergedIntoSlug SHALL equal T.Slug; T.UseCount SHALL reflect the combined count

#### Scenario: Target tag not found

- **WHEN** the `targetSlug` does not match any existing tag
- **THEN** the response SHALL be 404 with error code `TAG_NOT_FOUND`

#### Scenario: Source or target not active

- **WHEN** source or target tag is not `active` at merge time
- **THEN** the response SHALL be 409 with error code `TAG_NOT_ACTIVE`

#### Scenario: Self-merge rejected

- **WHEN** source and target Slugs are identical
- **THEN** the response SHALL be 400 with error code `TAG_SELF_MERGE`

### Requirement: Admin delete tag endpoint

The system SHALL expose `DELETE /api/forum/admin/tags/{id}` exclusively to admins. Deletion SHALL only be permitted when `UseCount == 0` and Status is `disabled` or `merged`.

#### Scenario: Zero-use disabled tag deleted

- **WHEN** an admin deletes a tag with UseCount 0 and Status `disabled`
- **THEN** the record SHALL be permanently removed from `forum_tags`

#### Scenario: Non-zero UseCount deletion rejected

- **WHEN** an admin attempts to delete a tag with UseCount > 0
- **THEN** the response SHALL be 409 with error code `TAG_IN_USE`

#### Scenario: Active tag deletion rejected

- **WHEN** an admin attempts to delete an `active` tag
- **THEN** the response SHALL be 409 with error code `TAG_MUST_BE_DISABLED`

### Requirement: Development seed endpoint

In non-production environments, the system SHALL expose `POST /api/forum/admin/tags/seed-from-posts` exclusively to admins, which aggregates all distinct tags from existing `forum_posts` and upserts them into `forum_tags` with Status `active` and computed UseCount. This endpoint SHALL NOT be registered when `ASPNETCORE_ENVIRONMENT` is `Production`.

#### Scenario: Seed creates tags from posts

- **WHEN** an admin calls the seed endpoint in a development environment
- **THEN** all distinct tag strings from forum_posts are upserted to forum_tags (existing tags are not overwritten), and UseCount is calculated from actual post data

#### Scenario: Seed unavailable in production

- **WHEN** the application runs with `ASPNETCORE_ENVIRONMENT=Production`
- **THEN** `POST /api/forum/admin/tags/seed-from-posts` SHALL return 404 (route not registered)
