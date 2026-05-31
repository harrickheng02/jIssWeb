## ADDED Requirements

### Requirement: Report-queue delete operations require reportId and do not notify authors

When `DELETE /api/mod/posts/{postId}` or `DELETE /api/mod/replies/{replyId}` is invoked with a JSON body (or equivalent documented request shape) that includes `reportId`, the handler SHALL require a resolvable `reportId` the caller is authorized to act on. The handler SHALL NOT require `reason` for delete operations. On success the moderation audit metadata SHALL include `reportId` in addition to existing board and target fields; optional internal `reason` MAY be stored when supplied but SHALL NOT be exposed to the content author. The system SHALL NOT insert in-app notifications to the deleted content author solely because of moderator deletion. Deletes invoked without `reportId` from non-report surfaces SHALL retain existing behavior per the base delete requirements.

#### Scenario: Report-queue delete persists audit linkage

- **WHEN** a moderator deletes a post via the report queue with body `{ "reportId": "r1" }`
- **THEN** the delete SHALL succeed when scope allows
- **AND** the audit record for `post.modDelete` SHALL include `metadata.reportId=r1`

#### Scenario: Report-queue delete without reason succeeds

- **WHEN** a delete request includes `reportId` but omits or leaves empty `reason`
- **THEN** the response SHALL succeed when scope allows
- **AND** no in-app notification SHALL be written to the content author for the deletion
