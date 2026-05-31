## ADDED Requirements

### Requirement: Create draft

The system SHALL expose `POST /api/forum/posts/drafts` for authenticated clients to create a draft post. A draft SHALL be stored in `forum_posts` with `State: "draft"` and SHALL NOT appear in any public-facing post list or detail endpoint. The `boardId` field is optional at draft-creation time; `title` and `body` are also optional (supporting partial saves). The draft `AuthorSubId` SHALL be set to JWT `sub`.

#### Scenario: Authenticated user creates draft

- **WHEN** an authenticated client sends POST to `/api/forum/posts/drafts` with any combination of optional title, body, boardId, and tags
- **THEN** the response SHALL be 200 with the new draft's `id` and `State: "draft"`

#### Scenario: Draft not visible in public post list

- **WHEN** any client (authenticated or anonymous) requests `GET /api/forum/posts`
- **THEN** posts with `State: "draft"` SHALL NOT appear in the response

#### Scenario: Draft not visible via public post detail

- **WHEN** a client that is NOT the draft author requests `GET /api/forum/posts/{draftId}`
- **THEN** the response SHALL be 404

#### Scenario: Draft author can read own draft via detail endpoint

- **WHEN** an authenticated client whose `sub` matches the draft's `AuthorSubId` requests `GET /api/forum/posts/{draftId}`
- **THEN** the response SHALL be 200 with the draft content and `State: "draft"`

#### Scenario: Unauthenticated draft create rejected

- **WHEN** a client without a valid Bearer token calls POST to `/api/forum/posts/drafts`
- **THEN** the response SHALL be 401

### Requirement: Update draft

The system SHALL expose `PUT /api/forum/posts/drafts/{draftId}` for authenticated clients to update a draft's title, body, boardId, and tags. Only the JWT `sub` matching the draft's `AuthorSubId` MAY update the draft.

#### Scenario: Author updates draft

- **WHEN** an authenticated client whose `sub` matches the draft's `AuthorSubId` sends a valid PUT body
- **THEN** the response SHALL be 200 with the updated draft content

#### Scenario: Non-author draft update rejected

- **WHEN** an authenticated client whose `sub` does NOT match the draft's `AuthorSubId` sends PUT
- **THEN** the response SHALL be 403 with error code `FORBIDDEN`

#### Scenario: Update non-existent draft

- **WHEN** a client sends PUT to a non-existent or already-published draftId
- **THEN** the response SHALL be 404

### Requirement: Delete draft

The system SHALL expose `DELETE /api/forum/posts/drafts/{draftId}` for authenticated clients to permanently remove a draft. Only the JWT `sub` matching the draft's `AuthorSubId` MAY delete the draft. Deletion SHALL be physical (not soft-delete) since drafts have no moderation history.

#### Scenario: Author deletes draft

- **WHEN** an authenticated client whose `sub` matches the draft's `AuthorSubId` sends DELETE
- **THEN** the response SHALL be 200 and the draft SHALL no longer exist

#### Scenario: Non-author draft delete rejected

- **WHEN** an authenticated client whose `sub` does NOT match the draft's `AuthorSubId` sends DELETE
- **THEN** the response SHALL be 403 with error code `FORBIDDEN`

### Requirement: Publish draft

The system SHALL expose `POST /api/forum/posts/drafts/{draftId}/publish` for authenticated clients to publish a draft as a live post. On successful publish, the `State` SHALL change from `"draft"` to `"published"`, `boardId` SHALL be validated (must match a configured board), and `title` and `body` SHALL be non-empty. The published post SHALL behave identically to a post created via `POST /api/forum/posts`.

#### Scenario: Author publishes valid draft

- **WHEN** an authenticated client whose `sub` matches the draft's `AuthorSubId` sends POST to `/publish` and the draft has a non-empty title, body, and a valid boardId
- **THEN** the response SHALL be 200 with `{ id, state: "published" }` and the post SHALL appear in the public post list

#### Scenario: Publish rejected when title or body missing

- **WHEN** an authenticated client sends POST to `/publish` for a draft missing title or body
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_INPUT`

#### Scenario: Publish rejected when boardId invalid

- **WHEN** an authenticated client sends POST to `/publish` for a draft with an unknown or missing boardId
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_BOARD_ID`

#### Scenario: Publish already-published post rejected

- **WHEN** a client sends POST to `/api/forum/posts/drafts/{id}/publish` where the post's State is already `"published"`
- **THEN** the response SHALL be 404 (the resource does not exist as a draft)

#### Scenario: Non-author publish rejected

- **WHEN** an authenticated client whose `sub` does NOT match the draft's `AuthorSubId` sends POST to `/publish`
- **THEN** the response SHALL be 403 with error code `FORBIDDEN`

### Requirement: List own drafts

The system SHALL expose `GET /api/forum/me/drafts` for authenticated clients returning a paginated list of the calling user's draft posts, ordered by `CreatedAtUtc` descending. Only posts with `State: "draft"` and `AuthorSubId` equal to JWT `sub` SHALL be returned.

#### Scenario: Authenticated user retrieves own drafts

- **WHEN** an authenticated client requests `GET /api/forum/me/drafts` with valid pagination
- **THEN** the response SHALL return only draft posts whose `AuthorSubId` matches `sub`, ordered by creation time descending

#### Scenario: Empty drafts list

- **WHEN** the user has no drafts
- **THEN** the response SHALL return success with an empty items array and `totalCount: 0`

#### Scenario: Invalid pagination rejected

- **WHEN** a client sends invalid page or pageSize to `GET /api/forum/me/drafts`
- **THEN** the response SHALL be 400 with the unified error contract and error code `INVALID_PAGINATION`

#### Scenario: Unauthenticated access rejected

- **WHEN** a client without a valid Bearer token requests `GET /api/forum/me/drafts`
- **THEN** the response SHALL be 401
