## MODIFIED Requirements

### Requirement: Audit log is queryable by target post

The system SHALL expose an authenticated moderation endpoint that returns audit records for a post thread when queried with `targetType=post` and a valid `targetId`. The endpoint SHALL support optional query parameters `action` (one or more known moderation action codes), `fromUtc`, and `toUtc` (ISO-8601 timestamps inclusive of `OccurredAtUtc`), and SHALL paginate results with `page` and `pageSize` using the existing pagination limits. When `fromUtc` is after `toUtc`, the response SHALL be HTTP 400 with the uniform error envelope. When `action` contains an unknown code, the response SHALL be HTTP 400. When no optional filters are supplied, behavior SHALL match the pre-change contract (all thread-scoped rows, paginated).

#### Scenario: Authorized caller queries audit by post id

- **WHEN** an authorized caller requests the audit query endpoint with `targetType=post` and a valid `targetId` for an existing post
- **THEN** the response SHALL be successful
- **AND** returned items SHALL include operator identity, actionLabel, target identifiers, and occurred-at timestamp
- **AND** the response SHALL include totalCount, page, and pageSize

#### Scenario: Unauthorized caller cannot read audit

- **WHEN** a client with a valid token that lacks an authorized forum moderation role requests the audit query endpoint
- **THEN** the response SHALL be 403 with the unified error contract

#### Scenario: Caller filters by action and time range

- **WHEN** an authorized caller requests the audit endpoint with `targetType=post`, a valid `targetId`, `action=post.setSticky`, and a `fromUtc`/`toUtc` range
- **THEN** every returned item SHALL have action matching the filter and `OccurredAtUtc` within the inclusive range
- **AND** items outside the filter SHALL NOT appear

#### Scenario: Invalid time range rejected

- **WHEN** an authorized caller supplies `fromUtc` later than `toUtc`
- **THEN** the response SHALL be HTTP 400 with the uniform error envelope

## ADDED Requirements

### Requirement: Post-thread audit includes user sanctions linked to the post

When the audit query endpoint is called with `targetType=post` and a post id, the system SHALL include moderation audit records whose `targetType` is `user` and whose `action` is `user.warn`, `user.mute`, or `user.unmute`, when `Metadata.postId` equals the queried post id. These records SHALL appear in the same paginated, sort-by-`OccurredAtUtc`-descending result set as post-scoped governance actions.

#### Scenario: Post audit lists author warning from report context

- **WHEN** a moderator issued a warning via a report tied to post `P1` and the sanction audit row includes `Metadata.postId=P1`
- **AND** an authorized caller queries audit for `targetId=P1`
- **THEN** the response SHALL include the `user.warn` record with actionLabel for account warning

#### Scenario: Unrelated user sanctions excluded

- **WHEN** a user sanction audit row lacks `Metadata.postId` equal to the queried post id
- **THEN** that row SHALL NOT appear in the post-thread audit response

### Requirement: Post-thread audit includes report workflow actions for the post

When the audit query endpoint is called with `targetType=post` and a post id, the system SHALL include moderation audit records whose `targetType` is `report` and whose `Metadata.postId` equals the queried post id, including actions `report.acknowledge`, `report.resolve`, and `report.reject`. Legacy rows with actions `report.statusChange` or historical `report.resolve` naming MAY remain readable until data ages out.

#### Scenario: Acknowledge and resolve appear on post audit

- **WHEN** a report targeting post `P1` was acknowledged and later resolved
- **AND** an authorized caller queries audit for `targetId=P1`
- **THEN** the response SHALL include both report workflow audit rows with user-facing action labels
