## MODIFIED Requirements

### Requirement: Model service exposes moderator sanction endpoints with audit

The Model service SHALL expose `POST /api/mod/users/{sub}/sanctions` and `POST /api/mod/reports/{reportId}/sanctions` for authenticated moderators and administrators. The body SHALL include `type` (`warning` or `mute`), non-empty `reason`, mandatory `reportId` when invoked from the report-queue governance flow, and for mutes `durationPreset` (`24h`, `7d`, or `30d`; default `24h` when omitted). Administrators MAY act on any user tied to a report in scope. Moderators SHALL succeed only when the linked report's `boardId` is within their moderator scope. On success the handler SHALL call the User service internal create API, append a moderation audit record with action `user.warn` or `user.mute` and metadata including `reportId`, `postId` (from the linked report's post context), `boardId`, `reason`, and sanction identifiers, and for warnings SHALL write a `ForumWarning` in-app notification to the target user.

#### Scenario: Moderator mutes user from report queue

- **WHEN** a moderator posts `{ "type": "mute", "durationPreset": "24h", "reason": "重复灌水", "reportId": "r1" }` for a report in their board scope
- **THEN** the response SHALL be successful
- **AND** a mute record SHALL exist in User service
- **AND** an audit row with action `user.mute` and `metadata.reportId=r1` and `metadata.postId` equal to the report's post context SHALL exist

#### Scenario: Warning from report includes post linkage metadata

- **WHEN** a warning is issued from a report targeting a post
- **THEN** the audit metadata SHALL include `postId` identifying that post for post-thread audit queries

#### Scenario: Report queue flow requires reportId

- **WHEN** a sanction request is classified as report-queue origin (request includes `reportId`)
- **AND** `reportId` is missing or does not resolve to an existing report
- **THEN** the response SHALL be HTTP 400

### Requirement: Model service exposes sanction revoke with audit

The Model service SHALL expose `POST /api/mod/users/{sub}/sanctions/{sanctionId}/revoke` with non-empty `revokeReason` in the body. On success the User service record SHALL set `revokedAtUtc` and revocation operator fields, and a moderation audit row with action `user.unmute` SHALL be appended with metadata including `revokeReason`, optional `reportId`, and when `reportId` is present also `postId` and `boardId` from the linked report.

#### Scenario: Revoke after mute from report retains post linkage

- **WHEN** a mute created from report `r1` tied to post `P1` is revoked
- **THEN** the `user.unmute` audit row SHALL include `metadata.postId=P1`
