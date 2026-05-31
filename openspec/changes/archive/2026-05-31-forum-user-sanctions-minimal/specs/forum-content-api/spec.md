## ADDED Requirements

### Requirement: Forum write endpoints reject actively muted users

Before processing any authenticated forum **content write** operation, the Model service SHALL resolve the caller's `sub` against the User service internal forum sanction status. Content writes SHALL include at minimum: creating posts, creating replies, author self-edit of posts or replies, draft create/update/publish paths documented in `forum-draft-lifecycle` and `forum-post-self-edit`. When `isMuted` is true, the service SHALL respond HTTP **403** with the uniform error envelope, error code **`FORUM_MUTED`**, and SHALL include `mutedUntilUtc` when available. Read operations (lists, detail, likes, favorites, report submission) SHALL NOT be blocked by mute.

#### Scenario: Muted user cannot create post

- **WHEN** a user with an active mute calls `POST /api/forum/posts` with a valid token
- **THEN** the response SHALL be HTTP 403 with code `FORUM_MUTED`

#### Scenario: Muted user can still read and report

- **WHEN** a user with an active mute calls `GET /api/forum/posts/{id}` or `POST /api/forum/reports`
- **THEN** the response SHALL NOT be rejected solely due to mute status

#### Scenario: Non-muted user writes normally

- **WHEN** a user without an active mute creates a reply
- **THEN** the write SHALL proceed under existing authorization rules
