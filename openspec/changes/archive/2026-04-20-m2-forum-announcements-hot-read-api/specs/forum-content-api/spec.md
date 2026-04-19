## ADDED Requirements

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
