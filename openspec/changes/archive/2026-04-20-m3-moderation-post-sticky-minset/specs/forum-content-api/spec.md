## ADDED Requirements

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

