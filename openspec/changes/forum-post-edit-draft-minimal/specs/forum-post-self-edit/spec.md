## ADDED Requirements

### Requirement: Post author self-edit

The system SHALL expose `PUT /api/forum/posts/{postId}` allowing the authenticated post author to update the post's title, body, and tags. Only the JWT `sub` matching the stored `AuthorSubId` MAY perform this operation. The endpoint SHALL set `UpdatedAtUtc` to the current UTC time on each successful edit.

#### Scenario: Author edits post successfully

- **WHEN** an authenticated client whose `sub` matches the post's `AuthorSubId` sends a valid PUT body with at least one of title, body, or tags
- **THEN** the response SHALL be 200 with the updated post summary and the `UpdatedAtUtc` field set to the edit time

#### Scenario: Non-author edit rejected

- **WHEN** an authenticated client whose `sub` does NOT match the post's `AuthorSubId` sends PUT to an existing published post
- **THEN** the response SHALL be 403 with the unified error contract and error code `FORBIDDEN`

#### Scenario: Edit of soft-deleted post rejected

- **WHEN** a client sends PUT to a post whose `State` is `"deleted"`
- **THEN** the response SHALL be 404 with the unified error contract

#### Scenario: Edit of own draft post allowed

- **WHEN** an authenticated client whose `sub` matches the draft's `AuthorSubId` sends PUT to a post whose `State` is `"draft"`
- **THEN** the response SHALL be 200 with the updated draft content

#### Scenario: Unauthenticated edit rejected

- **WHEN** a client without a valid Bearer token calls PUT on any post
- **THEN** the response SHALL be 401

### Requirement: Post edit tags UseCount delta update

When a post's tags are changed via `PUT /api/forum/posts/{postId}`, the system SHALL apply a differential UseCount update to the `forum_tags` registry: for each tag removed (in old set but not new set), the matching registry record's `UseCount` SHALL be decremented by 1; for each tag added (in new set but not old set), the matching registry record's `UseCount` SHALL be incremented by 1. Tags not present in the registry SHALL be silently skipped. Tags in the intersection (unchanged) SHALL NOT be touched.

#### Scenario: Tags delta on edit increments and decrements correctly

- **WHEN** an author edits a post changing tags from `[A, B]` to `[B, C]` and both A and C exist in `forum_tags`
- **THEN** tag A's `UseCount` SHALL be decremented by 1, tag C's `UseCount` SHALL be incremented by 1, and tag B's `UseCount` SHALL remain unchanged

#### Scenario: Tags delta skips unregistered tags

- **WHEN** an author removes a tag that does not exist in `forum_tags`
- **THEN** no `forum_tags` document is written and the edit SHALL still succeed

### Requirement: Reply author self-edit

The system SHALL expose `PUT /api/forum/posts/{postId}/replies/{replyId}` allowing the authenticated reply author to update the reply body. Only the JWT `sub` matching the stored reply `AuthorSubId` MAY perform this operation. The endpoint SHALL set reply `UpdatedAtUtc` to the current UTC time on each successful edit.

#### Scenario: Author edits reply successfully

- **WHEN** an authenticated client whose `sub` matches the reply's `AuthorSubId` sends a valid PUT body with non-empty body text
- **THEN** the response SHALL be 200 with the updated reply DTO including `UpdatedAtUtc`

#### Scenario: Non-author reply edit rejected

- **WHEN** an authenticated client whose `sub` does NOT match the reply's `AuthorSubId` sends PUT
- **THEN** the response SHALL be 403 with error code `FORBIDDEN`

#### Scenario: Edit reply on locked post allowed

- **WHEN** the parent post has `RepliesLocked: true` and the reply author sends PUT to edit their reply body
- **THEN** the response SHALL be 200 (reply lock only blocks NEW replies, not editing existing ones)

#### Scenario: Edit of soft-deleted reply rejected

- **WHEN** a client sends PUT to a reply whose `State` is `"deleted"`
- **THEN** the response SHALL be 404

#### Scenario: Edit reply on deleted post rejected

- **WHEN** a client sends PUT to a reply whose parent post `State` is `"deleted"`
- **THEN** the response SHALL be 404

### Requirement: UpdatedAtUtc returned in DTOs

The system SHALL include `UpdatedAtUtc` (nullable ISO 8601 UTC timestamp) in `PostDetailDto`, `PostListItemDto`, and `ReplyDto`. The field SHALL be `null` if the resource has never been edited.

#### Scenario: Edited post returns UpdatedAtUtc

- **WHEN** a client reads a post that has been edited at least once via `GET /api/forum/posts/{postId}`
- **THEN** the response SHALL include a non-null `updatedAtUtc` matching the time of the last edit

#### Scenario: Never-edited resource returns null UpdatedAtUtc

- **WHEN** a client reads a post or reply that has never been edited
- **THEN** `updatedAtUtc` SHALL be `null` in the response
