# forum-moderation-post-ops Specification

## Purpose
TBD - created by archiving change m3-moderation-post-sticky-minset. Update Purpose after archive.
## Requirements
### Requirement: Moderation role can set and unset sticky on a post
The system SHALL expose a moderation HTTP endpoint that allows authorized forum operators to set or unset a post's sticky status and persist the change.

#### Scenario: Admin sets sticky successfully
- **WHEN** a client with a valid Bearer token containing the forum role claim that authorizes administrator access calls the set-sticky endpoint for an existing post with `isSticky=true`
- **THEN** the response SHALL be successful
- **AND** the post's persisted sticky status SHALL be set to true
- **AND** subsequent reads of the post via public post detail and list endpoints SHALL reflect the sticky status

#### Scenario: Admin unsets sticky successfully
- **WHEN** a client with a valid Bearer token containing the forum role claim that authorizes administrator access calls the set-sticky endpoint for an existing post with `isSticky=false`
- **THEN** the response SHALL be successful
- **AND** the post's persisted sticky status SHALL be set to false
- **AND** subsequent reads of the post via public post detail and list endpoints SHALL reflect the sticky status

#### Scenario: Moderator sets sticky only within managed boards
- **WHEN** a client with a valid Bearer token containing the forum role claim that authorizes moderator access calls the set-sticky endpoint for an existing post
- **THEN** the system SHALL authorize the operation only when the post's board identity is within the caller's managed board set
- **AND** the response SHALL be 403 with the unified error contract when the post is outside that set

#### Scenario: Unauthenticated caller is rejected
- **WHEN** a client without a valid Bearer token calls the set-sticky endpoint
- **THEN** the response SHALL be 401 with the unified error contract

#### Scenario: Missing post is rejected
- **WHEN** an authorized caller calls the set-sticky endpoint for a non-existent post id
- **THEN** the response SHALL be 404 with the unified error contract

### Requirement: Sticky operations produce an audit log record
The system SHALL persist an audit log record for each successful sticky status change.

#### Scenario: Audit record includes operator, target, action, and time
- **WHEN** an authorized caller successfully changes a post's sticky status
- **THEN** the system SHALL persist an audit record that includes the operator identity from JWT `sub`, the target post id, an action name that distinguishes set vs unset, and an occurred-at timestamp

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

### Requirement: Featured operations are governed by the same access control as sticky operations
The system SHALL apply the same moderator board-scoped authorization rules to featured operations as it does to sticky operations, using `ForumModerationAccessService` as the access control authority.

#### Scenario: Admin can feature any post across all boards
- **WHEN** a client with a valid Bearer token containing the forum role claim that authorizes administrator access calls the set-featured endpoint for any existing post
- **THEN** the system SHALL authorize the operation regardless of the post's board

#### Scenario: Moderator cannot feature posts outside managed boards
- **WHEN** a client with a valid Bearer token containing the forum role claim that authorizes moderator (non-admin) access calls the set-featured endpoint for a post whose board is not within the caller's `forumBoardIds` JWT claim
- **THEN** the response SHALL be 403 with the unified error contract

### Requirement: Audit log queryable endpoint covers featured actions
The moderation audit query endpoint SHALL return featured action records (actionLabel "加精" and "取消精华") when queried by target post id, using the same query contract as sticky audit records.

#### Scenario: Audit query returns featured records alongside sticky records
- **WHEN** an authorized caller requests the audit query endpoint with `targetType=post` and a `targetId` for a post that has had both sticky and featured actions
- **THEN** the response SHALL include records for all action types (sticky, lock, featured) on that post
- **AND** each record SHALL include operator identity, actionLabel, target identifiers, and occurred-at timestamp

