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

### Requirement: Forum popular tags read-only endpoint

The system SHALL expose `GET /api/forum/tags/popular` for anonymous clients returning an ordered list of tag strings derived solely from persisted post `Tags` fields, ordered by descending occurrence count with deterministic tie-breaking for equal counts.

#### Scenario: Default popular tags

- **WHEN** a client requests `GET /api/forum/tags/popular` without optional parameters
- **THEN** the response SHALL return a JSON payload with a stable field contract documenting an array of tag strings and SHALL NOT invent tags that do not exist on any post

#### Scenario: Popular tags scoped by board

- **WHEN** a client requests `GET /api/forum/tags/popular` with a valid `boardId` matching configured forum boards
- **THEN** counts SHALL consider only posts whose persisted board label matches that board’s configured title for the given id

#### Scenario: Invalid board id on popular tags rejected

- **WHEN** a client sends an unknown `boardId` on `GET /api/forum/tags/popular`
- **THEN** the response SHALL be 400 with the unified error contract using the same error code semantics as `GET /api/forum/posts` for invalid board ids

### Requirement: Forum posts list optional tag filter

The system SHALL support an optional `tag` query parameter on `GET /api/forum/posts` that limits results to posts whose `Tags` list contains a tag equal to the trimmed value under the same case-insensitive equality rule used for persistence comparisons documented in implementation.

#### Scenario: Tag filter without other filters

- **WHEN** a client requests `GET /api/forum/posts` with valid pagination and a non-empty trimmed `tag`, and omits `boardId` and `q`
- **THEN** each returned item SHALL refer only to posts that include that tag

#### Scenario: Tag combined with board

- **WHEN** a client sends both a valid `boardId` and a non-empty trimmed `tag`
- **THEN** results SHALL be limited to posts that match both filters

#### Scenario: Tag combined with keyword search

- **WHEN** a client sends a non-empty trimmed `tag` and a valid non-empty `q` per `forum-post-search`
- **THEN** results SHALL match the keyword filter AND the tag filter AND any valid `boardId` filter together

#### Scenario: Tag combined with keyword search and board

- **WHEN** a client sends a non-empty trimmed `tag`, a valid non-empty `q` per `forum-post-search`, and a valid `boardId`
- **THEN** results SHALL match the keyword filter AND the tag filter AND the board filter together

#### Scenario: Whitespace-only tag rejected

- **WHEN** a client sends `tag` present in the query string but empty or whitespace-only after trimming
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAG_QUERY`

#### Scenario: No tag preserves existing list behavior

- **WHEN** a client omits `tag` entirely
- **THEN** the endpoint SHALL apply no tag-based filter and SHALL preserve list behavior for all other query parameters

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

### Requirement: Create post optional tags

When the authenticated create-post body includes an optional `tags` array of strings, the system SHALL trim entries, remove empties, deduplicate case-insensitively while preserving the first occurrence’s casing, allow at most 10 distinct tags, and reject any tag whose trimmed length exceeds 32 characters.

#### Scenario: Too many distinct tags rejected

- **WHEN** an authenticated client sends more than 10 distinct non-empty tags after normalization
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAGS`

#### Scenario: Tag exceeding max length rejected

- **WHEN** an authenticated client sends a tag whose trimmed length is greater than 32
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAGS`

#### Scenario: Valid tags omitted or empty array

- **WHEN** an authenticated client omits `tags` or sends an empty array
- **THEN** the response SHALL succeed when other fields are valid and the post SHALL be persisted with an empty tag list

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

### Requirement: Forum announcements read-only list endpoint

The system SHALL expose `GET /api/forum/announcements` for anonymous clients returning a JSON payload suitable for the homepage announcement module, with a stable per-item field contract and a configurable maximum number of items.

#### Scenario: List returns announcement fields

- **WHEN** a client requests `GET /api/forum/announcements` with supported query parameters
- **THEN** the response SHALL use the unified success result wrapper
- **AND** each item SHALL include a stable string `id`, string `title`, optional string `summary`, optional string `linkUrl`, RFC3339-like timestamp `publishedAtUtc`, and optional boolean `pinned`

#### Scenario: Empty announcements list

- **WHEN** no announcement records exist or none match the query
- **THEN** the response SHALL return success with an empty array

#### Scenario: Limit bounds

- **WHEN** a client sends `limit` outside the supported integer range for this endpoint
- **THEN** the response SHALL use the unified error contract with a non-success HTTP status

### Requirement: Forum posts list optional popularity sort

The system SHALL support an optional `sort` query parameter on `GET /api/forum/posts` for anonymous clients. Permitted values are `latest` and `hot`. When omitted or set to `latest`, ordering SHALL match the default chronological behavior of the posts list endpoint. When set to `hot`, the system SHALL order results by descending `LikeCount`, then descending `CommentCount`, then descending `ViewCount`, then descending `CreatedAtUtc`, then ascending `Id`, within the same filter set defined by other supported query parameters.

#### Scenario: Default chronological ordering

- **WHEN** a client requests `GET /api/forum/posts` without `sort` or with `sort=latest` and valid pagination
- **THEN** returned items SHALL be ordered by published time descending per existing list behavior

#### Scenario: Hot ordering is deterministic

- **WHEN** a client requests `GET /api/forum/posts` with `sort=hot` and valid pagination
- **THEN** returned items SHALL follow the hot ordering rule above for all items in the current page

#### Scenario: Invalid sort rejected

- **WHEN** a client sends `sort` with a value other than `latest` or `hot`
- **THEN** the response SHALL be 400 with the unified error contract

#### Scenario: Hot sort combines with board filter

- **WHEN** a client requests `GET /api/forum/posts` with `sort=hot`, valid pagination, and a valid configured `boardId`
- **THEN** hot ordering SHALL apply only within posts matching that board filter

#### Scenario: Keyword search ignores sort parameter

- **WHEN** a client requests `GET /api/forum/posts` with a valid non-empty keyword query `q` per `forum-post-search` and also sends `sort=hot`
- **THEN** the response SHALL follow keyword search list behavior and ordering from `forum-post-search`
- **AND** the `sort` parameter SHALL not alter search result ordering

### Requirement: Posts list and detail include sticky status
The forum content API SHALL return a sticky status field for posts so that clients can render a sticky marker consistently on list and detail views.

#### Scenario: List item includes sticky status
- **WHEN** a client requests `GET /api/forum/posts` with valid pagination and without keyword search `q`
- **THEN** each returned post summary item SHALL include a boolean field representing whether the post is sticky

#### Scenario: Detail includes sticky status
- **WHEN** a client requests `GET /api/forum/posts/{postId}` for an existing post
- **THEN** the returned post detail SHALL include a boolean field representing whether the post is sticky

### Requirement: Sticky posts are ordered first on non-search lists
For non-search post lists, the system SHALL order sticky posts before non-sticky posts, while preserving the existing secondary ordering rule within each group.

#### Scenario: Latest list groups sticky posts first
- **WHEN** a client requests `GET /api/forum/posts` without `q` and with `sort` omitted or `sort=latest`
- **THEN** the response SHALL order results by `isSticky` descending first
- **AND** within the sticky group and within the non-sticky group, the response SHALL follow the existing latest ordering semantics from the base forum content API contract

#### Scenario: Hot list groups sticky posts first
- **WHEN** a client requests `GET /api/forum/posts` without `q` and with `sort=hot`
- **THEN** the response SHALL order results by `isSticky` descending first
- **AND** within the sticky group and within the non-sticky group, the response SHALL follow the hot ordering semantics defined by the base forum content API contract

#### Scenario: Keyword search ordering is unchanged
- **WHEN** a client requests `GET /api/forum/posts` with a valid non-empty keyword query `q` per `openspec/specs/forum-post-search/spec.md`
- **THEN** the response SHALL follow the **Search result ordering** requirement in that spec (recency-first; `isSticky` for display only)
- **AND** the sticky status field SHALL be returned for display without changing search ordering

