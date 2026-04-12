## ADDED Requirements

### Requirement: Forum popular tags read-only endpoint

The system SHALL expose `GET /api/forum/tags/popular` for anonymous clients returning an ordered list of tag strings derived solely from persisted post `Tags` fields, ordered by descending occurrence count with deterministic tie-breaking for equal counts.

#### Scenario: Default popular tags

- **WHEN** a client requests `GET /api/forum/tags/popular` without optional parameters
- **THEN** the response SHALL return a JSON payload with a stable field contract documenting an array of tag strings and SHALL NOT invent tags that do not exist on any post

#### Scenario: Popular tags scoped by board

- **WHEN** a client requests `GET /api/forum/tags/popular` with a valid `boardId` matching configured forum boards
- **THEN** counts SHALL consider only posts whose persisted board label matches that board’s configured title for the given id

#### Scenario: Invalid board id on popular tags rejected

- **WHEN** a client sends an unknown `boardId` on `GET /api/forum/tags/popular`
- **THEN** the response SHALL be 400 with the unified error contract using the same error code semantics as `GET /api/forum/posts` for invalid board ids

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

### Requirement: Create post optional tags

When the authenticated create-post body includes an optional `tags` array of strings, the system SHALL trim entries, remove empties, deduplicate case-insensitively while preserving the first occurrence’s casing, allow at most 10 distinct tags, and reject any tag whose trimmed length exceeds 32 characters.

#### Scenario: Too many distinct tags rejected

- **WHEN** an authenticated client sends more than 10 distinct non-empty tags after normalization
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAGS`

#### Scenario: Tag exceeding max length rejected

- **WHEN** an authenticated client sends a tag whose trimmed length is greater than 32
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAGS`

#### Scenario: Valid tags omitted or empty array

- **WHEN** an authenticated client omits `tags` or sends an empty array
- **THEN** the response SHALL succeed when other fields are valid and the post SHALL be persisted with an empty tag list
