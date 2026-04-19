## Purpose

论坛帖子点赞、收藏、我的收藏与变更响应中的 `likeCount` / `likedByMe` / `favoritedByMe` 契约。

## Requirements

### Requirement: Like and favorite mutations are authenticated and idempotent

The system SHALL expose `POST` and `DELETE` under `/api/forum/posts/{postId}/like` and `/api/forum/posts/{postId}/favorite` for authenticated clients only. Duplicate `POST` for an existing relation SHALL NOT increase counters or return an error; duplicate `DELETE` when no relation exists SHALL succeed without error.

#### Scenario: Unauthenticated mutation rejected

- **WHEN** a client calls any like or favorite mutation without a valid Bearer token
- **THEN** the response SHALL be 401 with the unified error contract

#### Scenario: Missing post rejected

- **WHEN** an authenticated client calls a like or favorite mutation for a non-existent `postId`
- **THEN** the response SHALL be 404 with the unified error contract

### Requirement: Like count matches persisted likes

For each post, the persisted `likeCount` field SHALL equal the number of persisted like relations for that post after any successful like or unlike operation completes.

#### Scenario: First like increments count

- **WHEN** an authenticated user successfully creates a like relation for a post that had no prior like from that user
- **THEN** the post’s persisted `likeCount` SHALL increase by exactly one relative to its value immediately before the operation

#### Scenario: Unlike decrements count

- **WHEN** an authenticated user successfully removes an existing like relation
- **THEN** the post’s persisted `likeCount` SHALL decrease by exactly one relative to its value immediately before the operation
- **AND** `likeCount` SHALL NOT become negative

### Requirement: My favorites list endpoint

The system SHALL expose `GET /api/forum/me/favorites` for authenticated clients returning a paginated list of post summaries for posts the user has favorited, ordered by favorite creation time descending.

#### Scenario: Authenticated favorites list

- **WHEN** a client presents a valid Bearer token and requests `GET /api/forum/me/favorites` with supported pagination parameters
- **THEN** each item SHALL use the same summary field contract as the public posts list where applicable
- **AND** items SHALL only refer to posts that still exist and remain readable by the list contract

#### Scenario: Unauthenticated favorites list rejected

- **WHEN** a client without a valid token requests `GET /api/forum/me/favorites`
- **THEN** the response SHALL be 401

#### Scenario: Invalid pagination on favorites list

- **WHEN** a client sends invalid pagination parameters to `GET /api/forum/me/favorites`
- **THEN** the response SHALL use the unified error contract with a non-success HTTP status

### Requirement: Mutation responses return current engagement snapshot

Successful like, unlike, favorite, and unfavorite responses SHALL return a JSON body indicating the post id and current `likeCount` and booleans for whether the current user likes and favorites the post after the operation.

#### Scenario: Successful mutation response shape

- **WHEN** a like or favorite mutation completes with 2xx
- **THEN** the response body SHALL allow the client to read `likeCount`, `likedByMe`, and `favoritedByMe` for that post without an additional round trip
