## MODIFIED Requirements

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
- **THEN** the response SHALL include only published posts whose persisted board label matches that board's configured title

#### Scenario: Invalid board id on list rejected

- **WHEN** a client sends an unknown `boardId` on `GET /api/forum/posts`
- **THEN** the response SHALL be 400 with the unified error contract (e.g. code `INVALID_BOARD_ID`)

### Requirement: Forum post detail endpoint

The system SHALL expose `GET /api/forum/posts/{postId}` for anonymous clients returning post detail including body content needed for a detail view. The endpoint SHALL return 404 for posts with `State: "deleted"`. For posts with `State: "draft"`, the endpoint SHALL return 404 to anonymous clients but SHALL return 200 to the authenticated draft author.

#### Scenario: Detail for existing published post

- **WHEN** a client requests detail for an existing published post id
- **THEN** the response SHALL include the post body, metadata consistent with the list item, and `updatedAtUtc` (nullable)

#### Scenario: Detail for soft-deleted post returns 404

- **WHEN** any non-moderator client requests detail for a post with `State: "deleted"`
- **THEN** the response SHALL be 404 with the unified error contract

#### Scenario: Detail for missing post

- **WHEN** a client requests detail for a non-existent id
- **THEN** the response SHALL be 404 with the unified error contract

## ADDED Requirements

### Requirement: Posts and replies include updatedAtUtc in responses

All post list items (`PostListItemDto`), post detail (`PostDetailDto`), and reply items (`ReplyDto`) returned by the forum content API SHALL include an `updatedAtUtc` field (nullable ISO 8601 UTC timestamp). The field SHALL be `null` if the resource has never been edited after creation.

#### Scenario: Post list item includes updatedAtUtc

- **WHEN** a client requests `GET /api/forum/posts` and one or more returned posts have been edited
- **THEN** each edited post item SHALL include a non-null `updatedAtUtc`
- **AND** unedited post items SHALL have `updatedAtUtc: null`

#### Scenario: Reply list includes updatedAtUtc

- **WHEN** a client requests `GET /api/forum/posts/{postId}/replies` and one or more replies have been edited
- **THEN** each edited reply SHALL include a non-null `updatedAtUtc`
