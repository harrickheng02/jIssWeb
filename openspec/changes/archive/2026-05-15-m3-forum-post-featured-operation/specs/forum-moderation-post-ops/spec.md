## ADDED Requirements

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
