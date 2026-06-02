## Purpose

定义论坛标签注册表数据模型、状态机与 UseCount 维护规格。`forum_tags` 集合是所有论坛标签的权威来源，状态机仅有 `active` / `disabled` 两态。

## Requirements

### Requirement: Forum tag record data model

The system SHALL maintain a dedicated `forum_tags` MongoDB collection as the authoritative source for all forum tags. Each record SHALL contain: a unique `Id` (ObjectId as string), a `Name` (canonical display string), a `Slug` (normalized lowercase trimmed string, unique index), an optional `Description`, a `Status` enum (`active` | `disabled`), a `UseCount` integer ≥ 0, `CreatedAtUtc`, `CreatedBySub`, `UpdatedAtUtc`, and `UpdatedBySub`.

#### Scenario: Slug uniqueness enforced

- **WHEN** two tag records are created with names that normalize to the same slug (e.g., "AI编程" and "ai编程")
- **THEN** the second creation SHALL fail with a conflict error code `TAG_SLUG_CONFLICT`

### Requirement: Tag status state machine

A forum tag SHALL follow a two-state machine: `active` ↔ `disabled`. Transitions are: `active` → `disabled` (disable), `disabled` → `active` (enable). There is no terminal or irreversible state.

#### Scenario: Active tag can be disabled

- **WHEN** a moderator or admin sends a disable request for a tag with Status `active`
- **THEN** the tag's Status SHALL transition to `disabled`

#### Scenario: Disabled tag can be re-enabled

- **WHEN** a moderator or admin sends an enable request for a tag with Status `disabled`
- **THEN** the tag's Status SHALL transition to `active`

### Requirement: UseCount maintenance for registered tags

The system SHALL maintain `forum_tags.UseCount` as a best-effort count of non-deleted posts that include the tag's `Name` in their `Tags` array, updated on the write path for tags that exist in the registry. Tags used in posts but not present in `forum_tags` do not contribute to any UseCount. UseCount does NOT gate post creation (hybrid mode).

#### Scenario: UseCount incremented on post creation (registered tags only)

- **WHEN** an authenticated user creates a post with tags that match registered `forum_tags` records (by Name)
- **THEN** each matching `forum_tags` record's `UseCount` SHALL be incremented by 1; tags not found in the registry are silently skipped

#### Scenario: UseCount decremented on post deletion (registered tags only)

- **WHEN** a moderator or admin deletes a post that had tags
- **THEN** each `forum_tags` record whose Name matches a tag in the deleted post SHALL have its `UseCount` decremented by 1, minimum 0; unregistered tags are silently skipped

### Requirement: MongoDB indexes for forum_tags

The `forum_tags` collection SHALL maintain the following indexes to support efficient queries:

- Unique index on `Slug`
- Compound index on `Status` ascending + `UseCount` descending (supports popular and admin list queries)
- Text or prefix index on `Name` (supports suggest endpoint)

#### Scenario: Slug uniqueness enforced at DB level

- **WHEN** two concurrent create requests attempt to insert the same Slug
- **THEN** MongoDB's unique index SHALL reject one, and the API SHALL surface this as `TAG_SLUG_CONFLICT`
