## ADDED Requirements

### Requirement: Forum tag record data model

The system SHALL maintain a dedicated `forum_tags` MongoDB collection as the authoritative source for all forum tags. Each record SHALL contain: a unique `Id` (ObjectId as string), a `Name` (canonical display string), a `Slug` (normalized lowercase trimmed string, unique index), an optional `Description`, a `Status` enum (`active` | `disabled` | `merged`), an optional `MergedIntoSlug` (non-null only when `Status == "merged"`), a `UseCount` integer ≥ 0, `CreatedAtUtc`, `CreatedBySub`, `UpdatedAtUtc`, and `UpdatedBySub`.

#### Scenario: Slug uniqueness enforced

- **WHEN** two tag records are created with names that normalize to the same slug (e.g., "AI编程" and "ai编程")
- **THEN** the second creation SHALL fail with a conflict error code `TAG_SLUG_CONFLICT`

#### Scenario: MergedIntoSlug only on merged tags

- **WHEN** a tag's Status is not "merged"
- **THEN** `MergedIntoSlug` SHALL be null or absent in any API response

### Requirement: Tag status state machine

A forum tag SHALL follow the state machine: `active` → `disabled` (disable), `disabled` → `active` (enable), `active` → `merged` (merge). The `merged` state is terminal and SHALL NOT be reversed.

#### Scenario: Active tag can be disabled

- **WHEN** an admin sends a disable request for a tag with Status `active`
- **THEN** the tag's Status SHALL transition to `disabled`

#### Scenario: Disabled tag can be re-enabled

- **WHEN** an admin sends an enable request for a tag with Status `disabled`
- **THEN** the tag's Status SHALL transition to `active`

#### Scenario: Merged tag cannot be disabled or enabled

- **WHEN** an admin sends a disable or enable request for a tag with Status `merged`
- **THEN** the system SHALL return 409 with error code `TAG_ALREADY_MERGED`

#### Scenario: Active tag can be merged

- **WHEN** an admin merges a source tag (Status `active`) into a valid `active` target tag
- **THEN** the source tag's Status SHALL transition to `merged` and `MergedIntoSlug` SHALL be set to the target's Slug

#### Scenario: Cannot merge a tag into itself

- **WHEN** an admin sends a merge request where source and target slugs are identical
- **THEN** the system SHALL return 400 with error code `TAG_SELF_MERGE`

### Requirement: UseCount maintenance for registered tags

The system SHALL maintain `forum_tags.UseCount` as a best-effort count of non-deleted posts that include the tag's `Name` in their `Tags` array, updated on the write path for tags that exist in the registry. Tags used in posts but not present in `forum_tags` do not contribute to any UseCount. UseCount does NOT gate post creation (hybrid mode).

#### Scenario: UseCount incremented on post creation (registered tags only)

- **WHEN** an authenticated user creates a post with tags that match registered `forum_tags` records (by Name)
- **THEN** each matching `forum_tags` record's `UseCount` SHALL be incremented by 1; tags not found in the registry are silently skipped

#### Scenario: UseCount decremented on post deletion (registered tags only)

- **WHEN** a moderator or admin deletes a post that had tags
- **THEN** each `forum_tags` record whose Name matches a tag in the deleted post SHALL have its `UseCount` decremented by 1, minimum 0; unregistered tags are silently skipped

#### Scenario: UseCount recalculated after merge

- **WHEN** an admin merges source tag S into target tag T
- **THEN** after all posts are migrated, target T's `UseCount` SHALL reflect the count of all posts containing T's Name (including migrated posts)

### Requirement: Merge migrates post data

When a merge operation is executed, the system SHALL synchronously update all forum posts that reference the source tag's Name, replacing it with the target tag's Name in the post's `Tags` array.

#### Scenario: Posts updated on merge

- **WHEN** tag "AI编程" (Status `active`) is merged into tag "人工智能" (Status `active`)
- **THEN** all `forum_posts` records whose `Tags` array contains "AI编程" SHALL have that element replaced with "人工智能"
- **AND** the replacement SHALL complete within the same HTTP request that initiates the merge

#### Scenario: Merge does not duplicate existing target tag in post

- **WHEN** a post already contains both "AI编程" and "人工智能" and a merge of "AI编程" → "人工智能" is executed
- **THEN** the post's `Tags` array SHALL contain "人工智能" exactly once after migration (deduplication)

### Requirement: MongoDB indexes for forum_tags

The `forum_tags` collection SHALL maintain the following indexes to support efficient queries:

- Unique index on `Slug`
- Compound index on `Status` ascending + `UseCount` descending (supports popular and admin list queries)
- Text or prefix index on `Name` (supports suggest endpoint)

#### Scenario: Slug uniqueness enforced at DB level

- **WHEN** two concurrent create requests attempt to insert the same Slug
- **THEN** MongoDB's unique index SHALL reject one, and the API SHALL surface this as `TAG_SLUG_CONFLICT`
