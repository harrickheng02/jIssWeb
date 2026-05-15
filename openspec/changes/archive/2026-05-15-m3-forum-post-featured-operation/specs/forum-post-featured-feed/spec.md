## ADDED Requirements

### Requirement: Posts list and detail include featured status
The forum content API SHALL return a `isFeatured` boolean field for posts so that clients can render a featured badge consistently on list and detail views.

#### Scenario: List item includes featured status
- **WHEN** a client requests `GET /api/forum/posts` with valid pagination
- **THEN** each returned post summary item SHALL include a boolean field `isFeatured` representing whether the post is featured

#### Scenario: Detail includes featured status
- **WHEN** a client requests `GET /api/forum/posts/{postId}` for an existing post
- **THEN** the returned post detail SHALL include a boolean field `isFeatured` representing whether the post is featured

### Requirement: Posts list supports featured filter
The system SHALL support an optional `featured` query parameter on `GET /api/forum/posts`. When `featured=true` is present, results SHALL be limited to posts whose persisted `IsFeatured` is `true`. The `featured` filter SHALL be orthogonal to and combinable with `boardId`, `tag`, and `q` parameters.

#### Scenario: Featured filter returns only featured posts
- **WHEN** a client requests `GET /api/forum/posts` with `featured=true` and valid pagination, without other filters
- **THEN** each returned item SHALL have `isFeatured: true`
- **AND** the response SHALL use the unified success result wrapper

#### Scenario: Featured filter combined with board filter
- **WHEN** a client requests `GET /api/forum/posts` with `featured=true` and a valid `boardId`
- **THEN** results SHALL include only posts that are both featured and belong to the specified board

#### Scenario: Featured filter combined with tag filter
- **WHEN** a client requests `GET /api/forum/posts` with `featured=true` and a valid non-empty `tag`
- **THEN** results SHALL include only posts that are both featured and contain the specified tag

#### Scenario: Featured filter combined with keyword search
- **WHEN** a client requests `GET /api/forum/posts` with `featured=true` and a valid non-empty `q` per `forum-post-search`
- **THEN** results SHALL include only posts that are both featured and match the keyword filter
- **AND** result ordering SHALL follow keyword search ordering from `forum-post-search` (not featured ordering)

#### Scenario: Omitting featured preserves existing behavior
- **WHEN** a client omits `featured` entirely
- **THEN** the endpoint SHALL apply no featured-based filter and SHALL preserve list behavior for all other query parameters

#### Scenario: Invalid featured value rejected
- **WHEN** a client sends `featured` with a value other than `true` or `false`
- **THEN** the response SHALL be 400 with the unified error contract

### Requirement: Featured posts ordered by FeaturedAtUtc descending
When the featured filter is active (without keyword search `q`), the system SHALL order results by `FeaturedAtUtc` descending. Posts with null `FeaturedAtUtc` SHALL fall back to `CreatedAtUtc` descending. The `sort` parameter (`latest`/`hot`) SHALL be ignored when `featured=true` is active and `q` is absent.

#### Scenario: Featured feed is ordered by featured time
- **WHEN** a client requests `GET /api/forum/posts` with `featured=true`, valid pagination, and without `q`
- **THEN** returned items SHALL be ordered by `FeaturedAtUtc` descending
- **AND** items with null `FeaturedAtUtc` SHALL appear after items with non-null `FeaturedAtUtc`, ordered by `CreatedAtUtc` descending within that null group

#### Scenario: Featured feed ordering is independent of sort parameter
- **WHEN** a client requests `GET /api/forum/posts` with `featured=true`, `sort=hot`, and without `q`
- **THEN** returned items SHALL follow the featured ordering rule (FeaturedAtUtc descending) and SHALL NOT apply hot sort ordering
