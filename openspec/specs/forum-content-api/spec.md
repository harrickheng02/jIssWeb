## Purpose

定义论坛 MVP 最小 HTTP 契约：帖子列表与详情（公开）、发帖与回复（需登录），字段满足首页 Feed 卡片与详情页；身份以 JWT `sub` 为准。
## Requirements
### Requirement: Forum posts list endpoint

The system SHALL expose `GET /api/forum/posts` for anonymous clients returning a paginated list of post summaries suitable for homepage feed cards. The list SHALL only include posts with `State: "published"`. Posts with `State: "draft"` or `State: "deleted"` SHALL be excluded from all results.

#### Scenario: List returns feed fields

- **WHEN** a client requests `GET /api/forum/posts` with supported pagination query parameters
- **THEN** each item SHALL include identifiers, title, excerpt, author identity field aligned with `sub`, published time, optional board or category label, tags, numeric counters for likes, comments, and views, `updatedAtUtc` (nullable), and `isSticky` as defined by the implementation contract

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

The system SHALL expose `GET /api/forum/tags/popular` for anonymous clients returning an ordered list of tag strings sourced from the `forum_tags` registry, filtered to Status `active` only, ordered by `UseCount` descending with deterministic tie-breaking (by `Slug` ascending) for equal counts. The endpoint SHALL NOT aggregate tags dynamically from the `forum_posts` collection. The optional `boardId` parameter is accepted for API compatibility but ignored; tags are global across all boards.

#### Scenario: Default popular tags from registry

- **WHEN** a client requests `GET /api/forum/tags/popular` without optional parameters
- **THEN** the response SHALL return an array of tag Name strings for all `active` tags, ordered by UseCount descending

#### Scenario: Disabled tags excluded

- **WHEN** the `forum_tags` collection contains tags with Status `disabled`
- **THEN** those tags SHALL NOT appear in the `GET /api/forum/tags/popular` response

#### Scenario: Unknown boardId ignored

- **WHEN** a client sends an unknown `boardId` on `GET /api/forum/tags/popular`
- **THEN** the response SHALL be 200 with the global popular tag list (boardId is ignored)

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

The system SHALL expose `GET /api/forum/posts/{postId}` for anonymous clients returning post detail including body content needed for a detail view. The endpoint SHALL return 404 for posts with `State: "deleted"` to non-author, non-moderator clients. For posts with `State: "draft"`, the endpoint SHALL return 404 to anonymous clients but SHALL return 200 to the authenticated draft author. Moderators and administrators MAY read soft-deleted posts via authenticated detail access per `forum-soft-delete`.

#### Scenario: Detail for existing published post

- **WHEN** a client requests detail for an existing published post id
- **THEN** the response SHALL include the post body, metadata consistent with the list item, and `updatedAtUtc` (nullable)

#### Scenario: Detail for soft-deleted post returns 404

- **WHEN** any non-moderator, non-author client requests detail for a post with `State: "deleted"`
- **THEN** the response SHALL be 404 with the unified error contract

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

### Requirement: Create post optional tags — hybrid mode

When the authenticated create-post body includes an optional `tags` array of strings, the system SHALL normalize entries (trim, remove empties, deduplicate case-insensitively preserving first occurrence’s casing) and enforce the following limits. **No registry membership validation is performed** — users may freely supply any tag string (hybrid mode). For tags that exist in `forum_tags`, UseCount is updated as a side effect but does not gate the request.

#### Scenario: Too many distinct tags rejected

- **WHEN** an authenticated client sends more than 10 distinct non-empty tags after normalization
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAGS`

#### Scenario: Tag exceeding max length rejected

- **WHEN** an authenticated client sends a tag whose trimmed length is greater than 32
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAGS`

#### Scenario: Unregistered tag accepted

- **WHEN** an authenticated client sends a tag string that does not exist in `forum_tags`
- **THEN** the post SHALL be created successfully with that tag stored as-is (hybrid mode: free-form tags are allowed)

#### Scenario: Disabled tag accepted

- **WHEN** an authenticated client sends a tag that exists in `forum_tags` but has Status `disabled`
- **THEN** the post SHALL be created successfully (disabled status only hides the tag from suggest/popular; it does not block posting)

#### Scenario: Valid tags omitted or empty array

- **WHEN** an authenticated client omits `tags` or sends an empty array
- **THEN** the response SHALL succeed when other fields are valid and the post SHALL be persisted with an empty tag list

#### Scenario: Registered tags trigger UseCount update

- **WHEN** an authenticated client creates a post with tags that exist in `forum_tags`
- **THEN** the post SHALL be created successfully and each matching `forum_tags` record’s `UseCount` SHALL be incremented by 1 (tags not in the registry are silently skipped)

### Requirement: Forum tags suggest endpoint

The system SHALL expose `GET /api/forum/tags/suggest` for anonymous clients returning a list of active tag records whose Name or Slug contains the `q` query parameter (case-insensitive prefix or substring match), ordered by `UseCount` descending, limited to `limit` results (default 10, max 50). Only tags with Status `active` SHALL appear. Results serve as autocomplete hints; the client MAY allow users to submit tags not present in the results (hybrid mode).

#### Scenario: Suggestions returned for partial input

- **WHEN** a client sends `GET /api/forum/tags/suggest?q=AI&limit=5`
- **THEN** the response SHALL return at most 5 active tags whose Name or Slug contains "AI" (case-insensitive), ordered by UseCount descending

#### Scenario: Empty query returns top active tags

- **WHEN** a client omits `q` or sends an empty string
- **THEN** the response SHALL return the top `limit` active tags by UseCount

#### Scenario: Disabled tags excluded from suggest

- **WHEN** tags with Status `disabled` match the query
- **THEN** they SHALL NOT appear in the suggest response

### Requirement: Replies on a post

The system SHALL expose endpoints to list replies for a post and to create a reply, with create requiring authentication and author from `sub`. Public reply lists SHALL include only replies with `State: "published"`.

#### Scenario: List replies is public

- **WHEN** a client requests replies for an existing post
- **THEN** the response SHALL return a list of published replies with author identity, timestamps, and `updatedAtUtc` (nullable)

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

The system SHALL expose an authenticated HTTP endpoint that returns a paginated list of post summaries for which the persisted author key equals JWT `sub` and `State` is `"published"`, using the same summary field contract as the public posts list where applicable. Draft posts SHALL be returned only via `GET /api/forum/me/drafts` per `forum-draft-lifecycle`.

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

### Requirement: Forum write endpoints reject actively muted users

Before processing any authenticated forum **content write** operation, the Model service SHALL resolve the caller's `sub` against the User service internal forum sanction status. Content writes SHALL include at minimum: creating posts, creating replies, author self-edit of posts or replies, draft create/update/publish paths documented in `forum-draft-lifecycle` and `forum-post-self-edit`. When `isMuted` is true, the service SHALL respond HTTP **403** with the uniform error envelope, error code **`FORUM_MUTED`**, and SHALL include `mutedUntilUtc` when available. When sanction status cannot be retrieved from the User service (network error, timeout, or non-success response), the service SHALL respond HTTP **503** with error code **`SANCTION_SERVICE_UNAVAILABLE`** and SHALL NOT proceed with the write. Read operations (lists, detail, likes, favorites, report submission) SHALL NOT be blocked by mute.

#### Scenario: Muted user cannot create post

- **WHEN** a user with an active mute calls `POST /api/forum/posts` with a valid token
- **THEN** the response SHALL be HTTP 403 with code `FORUM_MUTED`

#### Scenario: Muted user can still read and report

- **WHEN** a user with an active mute calls `GET /api/forum/posts/{id}` or `POST /api/forum/reports`
- **THEN** the response SHALL NOT be rejected solely due to mute status

#### Scenario: Non-muted user writes normally

- **WHEN** a user without an active mute creates a reply
- **THEN** the write SHALL proceed under existing authorization rules

#### Scenario: Sanction service unavailable blocks write

- **WHEN** a forum content write is attempted and the User service sanction status query fails
- **THEN** the response SHALL be HTTP 503 with code `SANCTION_SERVICE_UNAVAILABLE`

### Requirement: Create post and reply anti-spam gates

Before persisting a new post via `POST /api/forum/posts` or a new reply via `POST /api/forum/posts/{postId}/replies`, the Model service SHALL enforce post/reply rate limits and blocked-word rules defined in `openspec/specs/forum-anti-spam-placeholder/spec.md`.

Processing order for create endpoints SHALL be: authentication → mute check (`BlockForumMuted`) → empty-field validation → rate-limit check → blocked-word check → remaining business validation → persist → rate-limit increment on success only.

Rate-limit rejection SHALL take precedence over blocked-word evaluation when both would apply.

#### Scenario: Create post consults anti-spam spec

- **WHEN** a client needs normative behavior for blocked words or create-post rate limits
- **THEN** the system SHALL treat `openspec/specs/forum-anti-spam-placeholder/spec.md` as the source of truth

#### Scenario: Create reply consults anti-spam spec

- **WHEN** a client needs normative behavior for blocked words or create-reply rate limits
- **THEN** the system SHALL treat `openspec/specs/forum-anti-spam-placeholder/spec.md` as the source of truth

#### Scenario: Rate limit before blocked word on same request

- **WHEN** a user exceeds the post create rate limit and also submits content that would hit a blocked word
- **THEN** the response SHALL be HTTP 429 with code `RATE_LIMITED`

