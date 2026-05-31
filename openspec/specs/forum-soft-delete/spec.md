## Purpose

定义帖子与回复软删除、作者自删、0 互动永久硬删除及后台定时清理策略。

## Requirements

### Requirement: Moderator post delete is soft delete

When a moderator deletes a post via the moderation API, the system SHALL set the post's `State` to `"deleted"` and record `DeletedAtUtc` and `DeletedBySub`, rather than removing the document from the database. The post SHALL NOT appear in any public-facing endpoint after soft deletion. Moderators and administrators SHALL see the full post content with a clear `State: "deleted"` indicator via moderation endpoints.

#### Scenario: Soft-deleted post hidden from public list

- **WHEN** a post's `State` is `"deleted"`
- **THEN** `GET /api/forum/posts` SHALL NOT include that post in its response

#### Scenario: Soft-deleted post returns 404 on public detail

- **WHEN** a non-author, non-moderator client requests `GET /api/forum/posts/{postId}` for a soft-deleted post
- **THEN** the response SHALL be 404

#### Scenario: Moderator sees deleted post with state indicator

- **WHEN** a moderator or administrator accesses the moderation post list or audit endpoint
- **THEN** soft-deleted posts SHALL appear with `state: "deleted"` and SHALL include their full content, `DeletedAtUtc`, and `DeletedBySub`

#### Scenario: Tags UseCount decremented on soft delete

- **WHEN** a post with tags is soft-deleted
- **THEN** each tag present in `forum_tags` SHALL have its `UseCount` decremented by 1 (same as permanent delete behavior)

### Requirement: Author self-delete post (soft delete)

The system SHALL expose `DELETE /api/forum/posts/{postId}` for authenticated clients to soft-delete their own published post. Only the JWT `sub` matching the stored `AuthorSubId` MAY perform this operation. The `State` SHALL be set to `"deleted"`, `DeletedAtUtc` and `DeletedBySub` SHALL be recorded. Tags `UseCount` SHALL be decremented by 1 for each tag (same as moderator delete). The post SHALL NOT appear in any public-facing list or detail endpoint after self-deletion, except to the author themselves.

#### Scenario: Author self-deletes own post

- **WHEN** an authenticated client whose `sub` matches `AuthorSubId` sends `DELETE /api/forum/posts/{postId}` for a published post
- **THEN** the response SHALL be 200, the post SHALL have `State: "deleted"`, and `DeletedBySub` SHALL equal `AuthorSubId`

#### Scenario: Non-author self-delete rejected

- **WHEN** an authenticated client whose `sub` does NOT match `AuthorSubId` sends DELETE
- **THEN** the response SHALL be 403 with error code `FORBIDDEN`

#### Scenario: Author views own self-deleted post

- **WHEN** an authenticated client whose `sub` matches both `AuthorSubId` AND `DeletedBySub` requests `GET /api/forum/posts/{postId}` for a self-deleted post
- **THEN** the response SHALL be 200 with full post content and `state: "deleted"`

#### Scenario: Other users cannot view author self-deleted post

- **WHEN** any client that is NOT the author requests `GET /api/forum/posts/{postId}` for an author-self-deleted post
- **THEN** the response SHALL be 404

### Requirement: Author self-delete reply (soft delete)

The system SHALL expose `DELETE /api/forum/posts/{postId}/replies/{replyId}` for authenticated clients to soft-delete their own reply. Only the JWT `sub` matching the reply's `AuthorSubId` MAY perform this operation. The reply `State` SHALL be set to `"deleted"`, `DeletedAtUtc` and `DeletedBySub` SHALL be recorded, and the parent post `CommentCount` SHALL be decremented by 1.

#### Scenario: Author self-deletes own reply

- **WHEN** an authenticated client whose `sub` matches the reply's `AuthorSubId` sends DELETE
- **THEN** the response SHALL be 200 and the reply SHALL no longer appear in `GET /api/forum/posts/{postId}/replies`

#### Scenario: Non-author reply self-delete rejected

- **WHEN** an authenticated client whose `sub` does NOT match the reply's `AuthorSubId` sends DELETE
- **THEN** the response SHALL be 403 with error code `FORBIDDEN`

### Requirement: Author permanent hard-delete of zero-engagement self-deleted post

The system SHALL expose `DELETE /api/forum/posts/{postId}/permanent` allowing the author to permanently hard-delete a post they previously self-deleted, provided the post has zero engagement (likes, comments, and favorites all equal 0). This gives users a way to fully erase low-traffic content for privacy. The endpoint SHALL cascade-delete associated replies, likes, favorites, and notifications.

#### Scenario: Author permanently deletes zero-engagement self-deleted post

- **WHEN** an authenticated client whose `sub` matches `AuthorSubId` AND `DeletedBySub`, the post `State` is `"deleted"`, and likes/comments/favorites are all 0
- **THEN** the response SHALL be 200 and the post SHALL be permanently removed from the database

#### Scenario: Permanent delete rejected when post has engagement

- **WHEN** the post has one or more likes, comments, or favorites
- **THEN** the response SHALL be 400 with error code `HAS_ENGAGEMENT`

#### Scenario: Permanent delete rejected when post was moderator-deleted

- **WHEN** `DeletedBySub` does NOT match `AuthorSubId` (i.e., deleted by a moderator)
- **THEN** the response SHALL be 403 with error code `FORBIDDEN`

### Requirement: Moderator reply delete is soft delete

When a moderator deletes a reply via the moderation API, the system SHALL set the reply's `State` to `"deleted"` and record `DeletedAtUtc` and `DeletedBySub`. Soft-deleted replies SHALL NOT appear in `GET /api/forum/posts/{postId}/replies`.

#### Scenario: Soft-deleted reply hidden from public reply list

- **WHEN** a reply's `State` is `"deleted"`
- **THEN** `GET /api/forum/posts/{postId}/replies` SHALL NOT include that reply

#### Scenario: Soft-deleted reply count not adjusted retroactively

- **WHEN** a reply is soft-deleted
- **THEN** the parent post's `CommentCount` SHALL be decremented by 1 to reflect the removal (consistent with previous hard-delete behavior)

### Requirement: Automated hard-delete cleanup

The system SHALL run a background service (`DraftCleanupBackgroundService`) that periodically hard-deletes posts and replies whose `State` is `"deleted"` and whose `DeletedAtUtc` is older than the configured retention period. The retention period SHALL be configurable via `Forum:SoftDelete:RetentionDays` (default 30 days). The cleanup SHALL also cascade-delete associated likes, favorites, and in-app notifications for cleaned-up posts.

#### Scenario: Soft-deleted post hard-deleted after retention period

- **WHEN** a post has `State: "deleted"` and `DeletedAtUtc` is older than `RetentionDays`
- **THEN** the background service SHALL permanently remove the post document and its associated replies, likes, favorites, and notifications from the database

#### Scenario: Recently soft-deleted post preserved

- **WHEN** a post has `State: "deleted"` and `DeletedAtUtc` is within the retention window
- **THEN** the background service SHALL NOT remove the post

#### Scenario: Posts with open reports not cleaned up

- **WHEN** a soft-deleted post has at least one associated report with `state: "open"`
- **THEN** the background service SHALL skip that post and log a warning, preserving it until the report is resolved

### Requirement: One-time State field migration

On service startup, the system SHALL execute an idempotent migration that sets `State: "published"` on all `forum_posts` and `forum_replies` documents that do not yet have a `State` field. This ensures all pre-existing documents are queryable by the State filter without data loss.

#### Scenario: Migration sets State on legacy documents

- **WHEN** the service starts for the first time after this change is deployed and legacy documents without a `State` field exist
- **THEN** all such documents SHALL have `State: "published"` written, and public list queries SHALL continue to return them

#### Scenario: Migration is idempotent

- **WHEN** the migration runs on a database where all documents already have `State` set
- **THEN** no documents SHALL be modified and the service SHALL start successfully
