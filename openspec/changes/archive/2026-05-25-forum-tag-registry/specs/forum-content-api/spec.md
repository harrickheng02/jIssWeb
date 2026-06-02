## MODIFIED Requirements

### Requirement: Forum popular tags read-only endpoint

The system SHALL expose `GET /api/forum/tags/popular` for anonymous clients returning an ordered list of tag strings sourced from the `forum_tags` registry, filtered to Status `active` only, ordered by `UseCount` descending with deterministic tie-breaking (by `Slug` ascending) for equal counts. The endpoint SHALL NOT aggregate tags dynamically from the `forum_posts` collection. The optional `boardId` parameter is accepted for API compatibility but ignored; tags are global across all boards.

#### Scenario: Default popular tags from registry

- **WHEN** a client requests `GET /api/forum/tags/popular` without optional parameters
- **THEN** the response SHALL return an array of tag Name strings for all `active` tags, ordered by UseCount descending

#### Scenario: Disabled or merged tags excluded

- **WHEN** the `forum_tags` collection contains tags with Status `disabled` or `merged`
- **THEN** those tags SHALL NOT appear in the `GET /api/forum/tags/popular` response

#### Scenario: Unknown boardId ignored

- **WHEN** a client sends an unknown `boardId` on `GET /api/forum/tags/popular`
- **THEN** the response SHALL be 200 with the global popular tag list (boardId is ignored)

### Requirement: Create post optional tags — hybrid mode

When the authenticated create-post body includes an optional `tags` array of strings, the system SHALL normalize entries (trim, remove empties, deduplicate case-insensitively preserving first occurrence's casing) and enforce the following limits. **No registry membership validation is performed** — users may freely supply any tag string (hybrid mode). For tags that exist in `forum_tags`, UseCount is updated as a side effect but does not gate the request.

#### Scenario: Too many distinct tags rejected

- **WHEN** an authenticated client sends more than 10 distinct non-empty tags after normalization
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAGS`

#### Scenario: Tag exceeding max length rejected

- **WHEN** an authenticated client sends a tag whose trimmed length is greater than 32
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_TAGS`

#### Scenario: Unregistered tag accepted

- **WHEN** an authenticated client sends a tag string that does not exist in `forum_tags`
- **THEN** the post SHALL be created successfully with that tag stored as-is (hybrid mode: free-form tags are allowed)

#### Scenario: Disabled tag accepted

- **WHEN** an authenticated client sends a tag that exists in `forum_tags` but has Status `disabled`
- **THEN** the post SHALL be created successfully (disabled status only hides the tag from suggest/popular; it does not block posting)

#### Scenario: Valid tags omitted or empty array

- **WHEN** an authenticated client omits `tags` or sends an empty array
- **THEN** the response SHALL succeed when other fields are valid and the post SHALL be persisted with an empty tag list

#### Scenario: Registered tags trigger UseCount update

- **WHEN** an authenticated client creates a post with tags that exist in `forum_tags`
- **THEN** the post SHALL be created successfully and each matching `forum_tags` record's `UseCount` SHALL be incremented by 1 (tags not in the registry are silently skipped)

## ADDED Requirements

### Requirement: Forum tags suggest endpoint

The system SHALL expose `GET /api/forum/tags/suggest` for anonymous clients returning a list of active tag records whose Name or Slug contains the `q` query parameter (case-insensitive prefix or substring match), ordered by `UseCount` descending, limited to `limit` results (default 10, max 20). Only tags with Status `active` SHALL appear. Results serve as autocomplete hints; the client MAY allow users to submit tags not present in the results (hybrid mode).

#### Scenario: Suggestions returned for partial input

- **WHEN** a client sends `GET /api/forum/tags/suggest?q=AI&limit=5`
- **THEN** the response SHALL return at most 5 active tags whose Name or Slug contains "AI" (case-insensitive), ordered by UseCount descending

#### Scenario: Empty query returns top active tags

- **WHEN** a client omits `q` or sends an empty string
- **THEN** the response SHALL return the top `limit` active tags by UseCount

#### Scenario: Disabled or merged tags excluded from suggest

- **WHEN** tags with Status `disabled` or `merged` match the query
- **THEN** they SHALL NOT appear in the suggest response
