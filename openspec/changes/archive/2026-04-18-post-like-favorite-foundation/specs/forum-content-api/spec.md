## ADDED Requirements

### Requirement: Post summaries include like count and per-user like and favorite flags

For `GET /api/forum/posts`, `GET /api/forum/posts/{postId}`, and the authenticated current-user posts list described in this spec, each post summary or detail object SHALL include `likeCount` (non-negative integer), `likedByMe` (boolean), and `favoritedByMe` (boolean) when the implementation returns those fields for that endpoint family.

#### Scenario: Anonymous list omits per-user flags

- **WHEN** a client requests `GET /api/forum/posts` without a Bearer token
- **THEN** the response MAY omit `likedByMe` and `favoritedByMe` on each item
- **AND** if present, both SHALL be false

#### Scenario: Authenticated list includes per-user flags

- **WHEN** a client requests `GET /api/forum/posts` with a valid Bearer token
- **THEN** each item SHALL include `likedByMe` and `favoritedByMe` consistent with persisted relations for JWT `sub` and that post id

#### Scenario: Anonymous detail omits per-user flags

- **WHEN** a client requests `GET /api/forum/posts/{postId}` without a Bearer token
- **THEN** the response MAY omit `likedByMe` and `favoritedByMe`
- **AND** if present, both SHALL be false

#### Scenario: Authenticated detail includes per-user flags

- **WHEN** a client requests `GET /api/forum/posts/{postId}` with a valid Bearer token
- **THEN** the response SHALL include `likedByMe` and `favoritedByMe` consistent with persisted relations for JWT `sub` and that post id

#### Scenario: Own posts list includes per-user flags

- **WHEN** a client requests the authenticated current-user posts list with a valid Bearer token
- **THEN** each item SHALL include `likedByMe` and `favoritedByMe` consistent with persisted relations for JWT `sub` and that post id
