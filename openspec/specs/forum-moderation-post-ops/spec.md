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
The system SHALL expose an authenticated moderation endpoint that returns audit records filtered by a target post id.

#### Scenario: Authorized caller queries audit by post id
- **WHEN** an authorized caller requests the audit query endpoint with `targetType=post` and a valid `targetId` for an existing post
- **THEN** the response SHALL be successful
- **AND** returned items SHALL include operator identity, action, target identifiers, and occurred-at timestamp

#### Scenario: Unauthorized caller cannot read audit
- **WHEN** a client with a valid token that lacks an authorized forum moderation role requests the audit query endpoint
- **THEN** the response SHALL be 403 with the unified error contract

