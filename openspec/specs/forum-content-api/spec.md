## Purpose

定义论坛 MVP 最小 HTTP 契约：帖子列表与详情（公开）、发帖与回复（需登录），字段满足首页 Feed 卡片与详情页；身份以 JWT `sub` 为准。

## Requirements

### Requirement: Forum posts list endpoint

The system SHALL expose `GET /api/forum/posts` for anonymous clients returning a paginated list of post summaries suitable for homepage feed cards.

#### Scenario: List returns feed fields

- **WHEN** a client requests `GET /api/forum/posts` with supported pagination query parameters
- **THEN** each item SHALL include identifiers, title, excerpt, author identity field aligned with `sub`, published time, optional board or category label, tags, and numeric counters for likes, comments, and views as defined by the implementation contract

#### Scenario: Invalid pagination rejected

- **WHEN** a client sends invalid page or page size parameters
- **THEN** the response SHALL use the unified error contract with a non-success HTTP status

#### Scenario: List filtered by configured board id

- **WHEN** a client requests `GET /api/forum/posts` with query `boardId` set to a known board id from configuration
- **THEN** the response SHALL include only posts whose persisted board label matches that board’s configured title

#### Scenario: Invalid board id on list rejected

- **WHEN** a client sends an unknown `boardId` on `GET /api/forum/posts`
- **THEN** the response SHALL be 400 with the unified error contract (e.g. code `INVALID_BOARD_ID`)

### Requirement: Forum posts list optional keyword search

Optional query parameter `q` on `GET /api/forum/posts` (keyword search, rate limits, and error codes) SHALL be specified in `openspec/specs/forum-post-search/spec.md` and SHALL combine with `boardId` and pagination as described there.

#### Scenario: Search behavior is defined in forum-post-search

- **WHEN** a client or implementer needs the normative contract for `q` on the posts list endpoint
- **THEN** the system SHALL treat `openspec/specs/forum-post-search/spec.md` as the source of truth for that behavior

### Requirement: Forum boards configuration endpoint

The system SHALL expose `GET /api/forum/boards` for anonymous clients returning the ordered list of board ids and display titles from service configuration (`Forum:Boards`).

#### Scenario: Boards list for UI

- **WHEN** a client requests `GET /api/forum/boards`
- **THEN** the response SHALL return items with stable `id` and human-readable `title`, excluding entries with empty id or title

### Requirement: Forum post detail endpoint

The system SHALL expose `GET /api/forum/posts/{postId}` for anonymous clients returning post detail including body content needed for a detail view.

#### Scenario: Detail for existing post

- **WHEN** a client requests detail for an existing post id
- **THEN** the response SHALL include the post body and metadata consistent with the list item

#### Scenario: Detail for missing post

- **WHEN** a client requests detail for a non-existent id
- **THEN** the response SHALL be 404 with the unified error contract

### Requirement: Create post requires authentication

The system SHALL expose `POST /api/forum/posts` that creates a post only when the request includes a valid Bearer token; the author identifier SHALL be taken from JWT claim `sub`.

#### Scenario: Authenticated user creates post

- **WHEN** an authenticated client sends a valid create-post body
- **THEN** the response SHALL return the created post id and SHALL persist author as `sub`

#### Scenario: Unauthenticated create rejected

- **WHEN** a client without a valid token calls `POST /api/forum/posts`
- **THEN** the response SHALL be 401

#### Scenario: Create post with board id

- **WHEN** an authenticated client sends `boardId` matching a configured board
- **THEN** the post SHALL be persisted with the board field set to that board’s configured title

#### Scenario: Create post with legacy board title

- **WHEN** an authenticated client omits `boardId` but sends `board` equal to a configured board title (case-insensitive)
- **THEN** the post SHALL be persisted with that board title

#### Scenario: Invalid board on create rejected

- **WHEN** an authenticated client sends an unknown `boardId` or a `board` string that does not match any configured title
- **THEN** the response SHALL be 400 with the unified error contract (e.g. `INVALID_BOARD_ID` or `INVALID_BOARD`)

### Requirement: Replies on a post

The system SHALL expose endpoints to list replies for a post and to create a reply, with create requiring authentication and author from `sub`.

#### Scenario: List replies is public

- **WHEN** a client requests replies for an existing post
- **THEN** the response SHALL return a list of replies with author identity and timestamps

#### Scenario: Create reply requires authentication

- **WHEN** an authenticated client creates a reply on an existing post
- **THEN** the reply SHALL be persisted with author from `sub`

#### Scenario: Reply on missing post rejected

- **WHEN** a client creates a reply for a non-existent post id
- **THEN** the response SHALL be 404

### Requirement: Identity alignment with JWT

The forum API SHALL use JWT `sub` as the canonical author key for all write operations and SHALL conform to `openspec/specs/token-identity-consistency`.

#### Scenario: Author field matches sub

- **WHEN** a post or reply is created by an authenticated user
- **THEN** stored and returned author identifiers SHALL match the token `sub`

### Requirement: Current user's posts list

The system SHALL expose an authenticated HTTP endpoint that returns a paginated list of post summaries for which the persisted author key equals JWT `sub`, using the same summary field contract as the public posts list where applicable.

#### Scenario: Authenticated user retrieves own posts

- **WHEN** a client presents a valid Bearer token and requests the current user's posts list with supported pagination parameters
- **THEN** each item SHALL include identifiers, title, excerpt, author identity field equal to `sub`, published time, board, tags, and counters consistent with the public list contract
- **AND** items SHALL be only posts whose stored author key equals `sub`

#### Scenario: Invalid pagination on own posts list

- **WHEN** a client sends invalid page or page size parameters to the current user's posts list
- **THEN** the response SHALL use the unified error contract with a non-success HTTP status

#### Scenario: Unauthenticated access to own posts list

- **WHEN** a client without a valid token requests the current user's posts list
- **THEN** the response SHALL be 401

### Requirement: Current user's replies list

The system SHALL expose an authenticated HTTP endpoint that returns a paginated list of replies whose persisted author key equals JWT `sub`, including identifiers, post reference, body preview or full body per implementation contract, and timestamps.

#### Scenario: Authenticated user retrieves own replies

- **WHEN** a client presents a valid Bearer token and requests the current user's replies list with supported pagination parameters
- **THEN** each item SHALL relate to a reply whose stored author key equals `sub`

#### Scenario: Invalid pagination on own replies list

- **WHEN** a client sends invalid page or page size parameters to the current user's replies list
- **THEN** the response SHALL use the unified error contract with a non-success HTTP status

#### Scenario: Unauthenticated access to own replies list

- **WHEN** a client without a valid token requests the current user's replies list
- **THEN** the response SHALL be 401

### Requirement: My-content endpoints align with identity spec

The forum API SHALL implement current-user post and reply list endpoints such that filtering uses only JWT `sub` and SHALL conform to `openspec/specs/token-identity-consistency` for user-scoped reads.

#### Scenario: No alternate user key on my-content lists

- **WHEN** the current user's posts or replies list is requested
- **THEN** the service SHALL NOT accept a distinct user identifier from the client as the primary filter when it differs from `sub`

### Requirement: Successful reply create satisfies in-app notification delivery

The forum API implementation hosted on the model service SHALL ensure that a successful reply creation triggers in-app notification persistence for the post author when required by `openspec/specs/in-app-notifications`, including suppression when the reply author is the post author.

#### Scenario: Forum reply contract implies notification side effect

- **WHEN** a client completes `POST` reply creation with 2xx and a persisted reply
- **THEN** the system's notification state SHALL match the scenarios in `in-app-notifications` for reply-to-post delivery
