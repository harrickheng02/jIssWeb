## ADDED Requirements

### Requirement: Homepage announcements module uses forum announcements API

The forum homepage shell SHALL load right-column announcement content only from the read-only announcements HTTP contract defined in `forum-content-api` (`GET /api/forum/announcements`), including loading, empty, and failure states consistent with other right-column modules.

#### Scenario: Announcements success and empty states

- **WHEN** the announcements request succeeds with one or more items
- **THEN** the announcement module SHALL render each item using fields returned by that API
- **WHEN** the announcements request succeeds with an empty list
- **THEN** the announcement module SHALL show an empty state distinct from loading and distinct from request failure

#### Scenario: Announcements request failure

- **WHEN** the announcements request fails
- **THEN** the announcement module SHALL show a failure state distinguishable from loading and from an empty successful list

### Requirement: Homepage hot content module uses hot post list contract

The forum homepage shell SHALL load right-column hot post summaries from `GET /api/forum/posts` using `sort=hot` and the same board scope as the central feed list uses for `boardId`, with a documented page size upper bound suitable for the sidebar. Each rendered hot item SHALL use the same post summary field contract as feed cards where applicable.

#### Scenario: Hot content aligns with board scope

- **WHEN** the user views the homepage or changes the selected entry in the left classification area
- **THEN** the client SHALL request hot posts using the same `boardId` query parameter semantics as the central feed list
- **AND** the client SHALL include `sort=hot` on that request

#### Scenario: Hot content empty data

- **WHEN** the hot posts request succeeds with an empty list of items
- **THEN** the hot content module SHALL show an empty state distinct from loading and distinct from request failure

#### Scenario: Hot content request failure

- **WHEN** the hot posts request fails
- **THEN** the hot content module SHALL show a failure state distinguishable from loading and from an empty successful list
