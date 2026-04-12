## Purpose

论坛帖子关键词搜索、分页与列表摘要一致、带 `q` 的请求限流。

## Requirements

### Requirement: Forum post search query parameter

The system SHALL support an optional case-insensitive keyword search on `GET /api/forum/posts` via a single query parameter named `q` that filters posts where the trimmed keyword matches the post title as a substring OR matches `AuthorSubId` as a substring.

#### Scenario: Search returns summaries consistent with feed list

- **WHEN** a client requests `GET /api/forum/posts` with valid `page`, `pageSize`, and a non-empty `q` after trimming
- **THEN** the response SHALL be a paginated list using the same post summary field contract as the non-search list endpoint
- **AND** each item SHALL refer only to posts that satisfy the keyword filter combined with any valid `boardId` filter when provided

#### Scenario: Search combined with valid board filter

- **WHEN** a client sends both `q` and a valid `boardId`
- **THEN** results SHALL be limited to posts in that board and matching `q`

#### Scenario: Empty search query rejected

- **WHEN** a client sends `q` present in the query string but empty or whitespace-only after trimming
- **THEN** the response SHALL be 400 with the unified error contract (e.g. code `INVALID_SEARCH_QUERY`)

#### Scenario: No q preserves existing list behavior

- **WHEN** a client omits `q` entirely
- **THEN** the endpoint SHALL behave as the existing forum posts list without keyword filtering

### Requirement: Search endpoint rate limiting

The system SHALL enforce a configurable rate limit on requests to `GET /api/forum/posts` that include a non-empty `q`, keyed primarily by client IP as observed by the server (or first `X-Forwarded-For` hop when trusted), and SHALL return 429 with the unified error contract when exceeded.

#### Scenario: Under limit succeeds

- **WHEN** a client performs search requests within the configured quota
- **THEN** normal search responses apply

#### Scenario: Over limit rejected

- **WHEN** a client exceeds the configured quota for search requests
- **THEN** the response SHALL be 429 with the unified error contract
