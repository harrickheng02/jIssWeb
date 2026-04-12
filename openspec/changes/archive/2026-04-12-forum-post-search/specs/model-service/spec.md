## ADDED Requirements

### Requirement: Forum search persistence support

The model service SHALL implement persistence and query support for `GET /api/forum/posts` with `q` as specified in `forum-post-search`, including MongoDB filters on post title and `AuthorSubId`, and SHALL add or use indexes appropriate to the chosen query pattern for list-by-search under expected data volumes.

#### Scenario: Search reads stored posts

- **WHEN** a search request is processed against persisted forum posts
- **THEN** the query SHALL read from the same post collection used for the public list
- **AND** author filtering SHALL use the stored `AuthorSubId` field aligned with JWT `sub` per `token-identity-consistency`
