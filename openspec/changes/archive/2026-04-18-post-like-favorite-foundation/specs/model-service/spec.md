## ADDED Requirements

### Requirement: Forum post likes and favorites persistence

The model service SHALL persist forum post likes and favorites in MongoDB using separate collections or clearly separated document types with unique constraints preventing duplicate `(PostId, UserSubId)` pairs per kind, storing `UserSubId` aligned with JWT `sub`.

#### Scenario: Like document uniqueness

- **WHEN** two concurrent requests attempt to insert a like for the same user and post
- **THEN** at most one like document SHALL exist for that pair

#### Scenario: Favorite document uniqueness

- **WHEN** two concurrent requests attempt to insert a favorite for the same user and post
- **THEN** at most one favorite document SHALL exist for that pair

### Requirement: Forum like and favorite HTTP surface

The model service SHALL implement the like, favorite, and my-favorites endpoints specified in `openspec/changes/post-like-favorite-foundation/specs/forum-post-like-favorite/spec.md` under the same `/api/forum` prefix as existing forum routes, with JWT validation consistent with other protected forum routes.

#### Scenario: New routes require bearer

- **WHEN** a client calls `POST` or `DELETE` on `/api/forum/posts/{postId}/like` or `/api/forum/posts/{postId}/favorite`, or `GET /api/forum/me/favorites`, without a valid Bearer token
- **THEN** the response SHALL be 401

#### Scenario: Public read remains for existing GET forum routes

- **WHEN** a client calls existing public forum `GET` routes unchanged by this change
- **THEN** authentication requirements SHALL match the prior `forum-content-api` behavior

### Requirement: Forum reads include engagement fields

The model service SHALL implement `GET /api/forum/posts`, `GET /api/forum/posts/{postId}`, and the authenticated current-user posts list such that responses satisfy `openspec/changes/post-like-favorite-foundation/specs/forum-content-api/spec.md` for `likeCount`, `likedByMe`, and `favoritedByMe`.

#### Scenario: List and detail include likeCount

- **WHEN** a client requests `GET /api/forum/posts` or `GET /api/forum/posts/{postId}` for an existing post
- **THEN** the returned post object SHALL include `likeCount` equal to the persisted count for that post
