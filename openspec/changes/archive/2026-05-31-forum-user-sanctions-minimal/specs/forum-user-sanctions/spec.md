## ADDED Requirements

### Requirement: User service persists forum warnings and timed mutes

The User service SHALL persist forum account sanctions in a `user_sanctions` collection. Each record SHALL include `sub`, `type` (`warning` or `mute`), non-empty `reason`, `operatorSub`, `startsAtUtc`, optional `reportId`, optional `durationPreset` (`24h`, `7d`, or `30d` for mutes), optional `expiresAtUtc` (required for mutes, computed server-side from preset), optional `revokedAtUtc`, optional `revokedBySub`, and optional `revokeReason`. Warning records SHALL NOT block forum writes. Mute records SHALL block forum writes while active per enforcement rules in `forum-content-api`.

#### Scenario: Mute record stores server-computed expiry

- **WHEN** an internal create request specifies `type=mute` and `durationPreset=24h`
- **THEN** the persisted record SHALL have `expiresAtUtc` equal to `startsAtUtc` plus twenty-four hours UTC
- **AND** the client-supplied arbitrary timestamp for expiry SHALL NOT be honored

#### Scenario: Warning record has no expiry

- **WHEN** an internal create request specifies `type=warning`
- **THEN** the persisted record SHALL NOT require `expiresAtUtc`
- **AND** the record SHALL remain for audit history after creation

### Requirement: Internal forum sanction status query

The User service SHALL expose `GET /api/internal/users/{sub}/forum-sanction-status` authenticated by a shared internal service key (header contract documented in deployment config). The response SHALL include `isMuted` (boolean), `mutedUntilUtc` (nullable ISO timestamp when muted), and `activeWarningCount` (count of non-revoked warning records for rolling audit; enforcement MAY use zero). A mute SHALL be active when there exists a mute record with `revokedAtUtc` null and `UtcNow < expiresAtUtc`.

#### Scenario: Active mute returns blocked status

- **WHEN** internal status is queried for a user with an unrevoked mute where `UtcNow` is before `expiresAtUtc`
- **THEN** `isMuted` SHALL be true
- **AND** `mutedUntilUtc` SHALL equal that record's `expiresAtUtc`

#### Scenario: Expired mute returns not muted

- **WHEN** internal status is queried and all mute records are revoked or past `expiresAtUtc`
- **THEN** `isMuted` SHALL be false

#### Scenario: Missing or invalid internal key rejected

- **WHEN** a caller omits or supplies an incorrect internal service key
- **THEN** the response SHALL be HTTP 401

### Requirement: Moderators and admins issue warnings and mutes from Model API

The Model service SHALL expose `POST /api/mod/users/{sub}/sanctions` for authenticated moderators and administrators. The body SHALL include `type` (`warning` or `mute`), non-empty `reason`, mandatory `reportId` when invoked from the report-queue governance flow, and for mutes `durationPreset` (`24h`, `7d`, or `30d`; default `24h` when omitted). Administrators MAY act on any user tied to a report in scope. Moderators SHALL succeed only when the linked report's `boardId` is within their moderator scope. On success the handler SHALL call the User service internal create API, append a moderation audit record with action `user.warn` or `user.mute` and metadata including `reportId`, `reason`, and sanction identifiers, and for warnings SHALL write a `ForumWarning` in-app notification to the target user.

#### Scenario: Moderator mutes author from report queue

- **WHEN** a moderator posts `{ "type": "mute", "durationPreset": "24h", "reason": "重复灌水", "reportId": "r1" }` for a report in their board scope
- **THEN** the response SHALL succeed
- **AND** a mute sanction SHALL exist for the target user
- **AND** an audit row with action `user.mute` and `metadata.reportId=r1` SHALL exist

#### Scenario: Empty reason rejected

- **WHEN** a caller submits a sanction request with blank `reason`
- **THEN** the response SHALL be HTTP 400 with a documented error code

#### Scenario: Report queue flow requires reportId

- **WHEN** a sanction request is classified as report-queue origin (request includes `reportId`)
- **AND** `reportId` is missing or does not resolve to an existing report
- **THEN** the response SHALL be HTTP 400

### Requirement: Moderators and admins may revoke mutes early

The Model service SHALL expose `POST /api/mod/users/{sub}/sanctions/{sanctionId}/revoke` with non-empty `revokeReason` in the body. On success the User service record SHALL set `revokedAtUtc` and revocation operator fields, and a moderation audit row with action `user.unmute` SHALL be appended with metadata including `revokeReason` and optional `reportId`.

#### Scenario: Early revoke clears active mute

- **WHEN** an authorized caller revokes an active mute with a non-empty reason
- **THEN** subsequent internal status queries for that user SHALL return `isMuted=false`

#### Scenario: Revoke without reason rejected

- **WHEN** revoke is called with empty `revokeReason`
- **THEN** the response SHALL be HTTP 400
