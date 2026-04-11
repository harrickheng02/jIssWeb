## Purpose

定义论坛 MVP 最小 HTTP 契约：帖子列表与详情（公开）、发帖与回复（需登录），字段满足首页 Feed 卡片与详情页；身份以 JWT `sub` 为准。

## ADDED Requirements

### Requirement: Forum posts list endpoint

The system SHALL expose `GET /api/forum/posts` for anonymous clients returning a paginated list of post summaries suitable for homepage feed cards.

#### Scenario: List returns feed fields

- **WHEN** a client requests `GET /api/forum/posts` with supported pagination query parameters
- **THEN** each item SHALL include identifiers, title, excerpt, author identity field aligned with `sub`, published time, optional board or category label, tags, and numeric counters for likes, comments, and views as defined by the implementation contract

#### Scenario: Invalid pagination rejected

- **WHEN** a client sends invalid page or page size parameters
- **THEN** the response SHALL use the unified error contract with a non-success HTTP status

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
