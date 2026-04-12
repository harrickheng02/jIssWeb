## ADDED Requirements

### Requirement: Homepage hot tags use persisted tag vocabulary

The forum homepage shell SHALL obtain right-column hot tag labels only from the read-only popular-tags HTTP contract defined in `forum-content-api`, scoped consistently with the active left classification board filter (all boards versus a specific configured board id).

#### Scenario: Hot tags align with board scope

- **WHEN** the user views the homepage or changes the selected entry in the left classification area
- **THEN** the client SHALL request popular tags using the same board scope as the central feed list uses for `boardId`
- **AND** each rendered hot tag label SHALL equal a tag string returned by that response

#### Scenario: Hot tags empty data

- **WHEN** the popular-tags response succeeds with an empty list
- **THEN** the hot-tags module SHALL show an empty state distinct from loading and distinct from request failure

#### Scenario: Hot tags request failure

- **WHEN** the popular-tags request fails
- **THEN** the hot-tags module SHALL show a failure state that is distinguishable from loading and from an empty successful list

### Requirement: Homepage hot tag selection drives central feed tag filter

The forum homepage shell SHALL update the central post feed when the user selects a hot tag so that list requests include the optional `tag` query parameter on `GET /api/forum/posts` as specified in `forum-content-api`, combined with any active `boardId` and optional keyword `q` filters using the same AND semantics as the server list endpoint.

#### Scenario: Selecting a hot tag filters the feed

- **WHEN** the user selects a rendered hot tag
- **THEN** the client SHALL issue feed requests that include the corresponding `tag` query value
- **AND** the feed SHALL only display posts returned by the server for that combined filter set

#### Scenario: Clearing active tag filter

- **WHEN** the user clears the active tag filter using the provided homepage control
- **THEN** the client SHALL omit the `tag` query parameter from subsequent feed requests
- **AND** existing board and keyword search behavior SHALL remain unchanged

#### Scenario: Post card tag uses the same feed tag filter as hot tags

- **WHEN** the user selects a tag rendered on a post summary card in the central feed
- **THEN** the client SHALL apply the same `tag` query parameter update and list refresh behavior as when selecting a hot tag in the right column
