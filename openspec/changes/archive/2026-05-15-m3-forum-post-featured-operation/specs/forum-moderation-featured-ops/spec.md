## ADDED Requirements

### Requirement: Moderation role can set and unset featured on a post
The system SHALL expose a moderation HTTP endpoint `POST /api/mod/posts/{id}/featured` that allows authorized forum operators to set or unset a post's featured status and persist the change, including `IsFeatured`, `FeaturedAtUtc`, and `FeaturedBySub` fields on the post record.

#### Scenario: Moderator sets featured successfully
- **WHEN** a client with a valid Bearer token containing the forum role claim that authorizes moderation access calls `POST /api/mod/posts/{id}/featured` with `{ "isFeatured": true }` for an existing post
- **THEN** the response SHALL be successful
- **AND** the post's persisted `IsFeatured` SHALL be set to `true`
- **AND** `FeaturedAtUtc` SHALL be set to the current UTC timestamp
- **AND** `FeaturedBySub` SHALL be set to the caller's JWT `sub`
- **AND** subsequent reads of the post via public post detail and list endpoints SHALL reflect `isFeatured: true`

#### Scenario: Moderator unsets featured successfully
- **WHEN** a client with a valid Bearer token containing the forum role claim that authorizes moderation access calls `POST /api/mod/posts/{id}/featured` with `{ "isFeatured": false }` for an existing post
- **THEN** the response SHALL be successful
- **AND** the post's persisted `IsFeatured` SHALL be set to `false`
- **AND** `FeaturedAtUtc` and `FeaturedBySub` SHALL be cleared to null
- **AND** subsequent reads of the post via public post detail and list endpoints SHALL reflect `isFeatured: false`

#### Scenario: Moderator sets featured only within managed boards
- **WHEN** a client with a valid Bearer token containing the forum role claim that authorizes moderator (non-admin) access calls the set-featured endpoint for an existing post
- **THEN** the system SHALL authorize the operation only when the post's board identity is within the caller's managed board set
- **AND** the response SHALL be 403 with the unified error contract when the post is outside that set

#### Scenario: Unauthenticated caller is rejected
- **WHEN** a client without a valid Bearer token calls `POST /api/mod/posts/{id}/featured`
- **THEN** the response SHALL be 401 with the unified error contract

#### Scenario: Missing post is rejected
- **WHEN** an authorized caller calls the set-featured endpoint for a non-existent post id
- **THEN** the response SHALL be 404 with the unified error contract

### Requirement: Featured operations produce an audit log record
The system SHALL persist an audit log record for each successful featured status change, using actionLabel "加精" for set and "取消精华" for unset.

#### Scenario: Audit record distinguishes set vs unset
- **WHEN** an authorized caller successfully sets featured status to `true`
- **THEN** the system SHALL persist an audit record with operator identity from JWT `sub`, target post id, actionLabel "加精", and occurred-at timestamp

#### Scenario: Audit record for unset
- **WHEN** an authorized caller successfully sets featured status to `false`
- **THEN** the system SHALL persist an audit record with operator identity from JWT `sub`, target post id, actionLabel "取消精华", and occurred-at timestamp
