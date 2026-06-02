# forum-moderation-audit-feed Specification

## Purpose
TBD - created by archiving change forum-moderation-audit-global-feed. Update Purpose after archive.
## Requirements
### Requirement: Global moderation audit feed API

The system SHALL expose `GET /api/mod/audit/feed` on **JIssWeb.Model.Api** for authenticated clients whose effective forum role is `moderator` or `admin` per `token-identity-consistency`. The handler SHALL return a paginated list of moderation audit records sorted by `occurredAtUtc` descending. Supported query parameters SHALL be: `page` (default 1), `pageSize` (default 20, maximum 50), optional `action` (repeatable or comma-separated, each value MUST be a known moderation action code), optional `fromUtc` and `toUtc` (ISO-8601 UTC; when both omitted the server SHALL default to the most recent `Forum:ModerationAudit:DefaultFeedDays` calendar days ending at request time UTC), and optional `boardId` to narrow results to a single board. Invalid pagination, action, time range (`fromUtc > toUtc`), or time format SHALL return HTTP 400 with the uniform error envelope and documented codes consistent with the existing per-post audit endpoint. The response SHALL use the standard success envelope with items containing at minimum: audit id, target type, target id, user-facing action label, operator display name, `occurredAtUtc`, board identifier and board label when resolvable, associated `postId` when present in metadata, and associated `reportId` when present in metadata.

#### Scenario: Admin lists site-wide feed with default time window

- **WHEN** an admin calls `GET /api/mod/audit/feed` without `boardId` and without `fromUtc`/`toUtc`
- **THEN** the response SHALL be HTTP 200 with items from all boards within the default time window
- **AND** items SHALL be ordered by `occurredAtUtc` descending

#### Scenario: Moderator default scope is authorized boards only

- **WHEN** a moderator calls `GET /api/mod/audit/feed` without `boardId`
- **THEN** every returned item SHALL have `metadata.boardId` within the caller's JWT `forumBoardIds` scope
- **AND** items lacking a resolvable `metadata.boardId` SHALL NOT be returned

#### Scenario: Moderator narrows to one board

- **WHEN** a moderator calls the feed with `boardId` set to a board in their `forumBoardIds`
- **THEN** only audit rows for that board SHALL be returned

#### Scenario: Moderator requests out-of-scope board

- **WHEN** a moderator calls the feed with `boardId` not in their `forumBoardIds`
- **THEN** the response SHALL be HTTP 403 with code `FORBIDDEN`

#### Scenario: Action and time filters apply

- **WHEN** a caller supplies `action=report.resolve` and a valid `fromUtc`/`toUtc` range
- **THEN** only matching rows within the time range SHALL be returned

#### Scenario: Member cannot access feed

- **WHEN** a member-role token calls the feed
- **THEN** the response SHALL be HTTP 403

#### Scenario: Unfiltered feed excludes audit.export noise

- **WHEN** a caller requests the feed or export without an `action` filter
- **THEN** rows with action `audit.export` SHALL NOT be included
- **WHEN** a caller explicitly filters with `action=audit.export`
- **THEN** matching `audit.export` rows MAY be returned

### Requirement: Moderation audit CSV export

The system SHALL expose `GET /api/mod/audit/export` with the same authentication, authorization, and query parameters as the feed endpoint (`action`, `fromUtc`, `toUtc`, `boardId`, and implicit default time window). On success the response SHALL be HTTP 200 with `Content-Type: text/csv; charset=utf-8` and a downloadable attachment. The CSV SHALL be UTF-8 and include a header row plus one row per audit record up to `Forum:ModerationAudit:MaxExportRows` (default 5000), ordered by `occurredAtUtc` descending to match the feed list. Columns SHALL include at minimum: occurred time (UTC ISO-8601), action label (Chinese), operator display name, operator sub, target type, target id, board id, board label, post id, report id (empty when absent). When the matching row count exceeds the configured maximum, the response SHALL be HTTP 400 with code `EXPORT_TOO_LARGE` and no file body.

#### Scenario: Admin exports CSV for current filters

- **WHEN** an admin calls export with the same filters as a successful feed query under the row limit
- **THEN** the response SHALL be HTTP 200 with a CSV attachment
- **AND** row count SHALL equal the number of matching audit documents

#### Scenario: Export too large is rejected

- **WHEN** matching documents exceed `MaxExportRows`
- **THEN** the response SHALL be HTTP 400 with code `EXPORT_TOO_LARGE`

#### Scenario: Moderator export respects board scope

- **WHEN** a moderator exports without `boardId`
- **THEN** the CSV SHALL include only rows whose board is within `forumBoardIds`

### Requirement: Audit export is logged

After a successful CSV export, the system SHALL insert a moderation audit row with action `audit.export`, `targetType` `system`, `targetId` equal to a stable export correlation id, `operatorSub` equal to the caller, and metadata containing the filter summary (time range, actions, boardId if any) and exported row count. Failure to write this audit row SHALL NOT fail the HTTP export response; failures SHALL be logged.

#### Scenario: Successful export writes audit.export

- **WHEN** an authorized export completes with N rows
- **THEN** an `audit.export` audit record SHALL exist for that operation with exported count N in metadata

### Requirement: Per-post audit endpoint remains unchanged

The existing `GET /api/mod/audit` contract requiring `targetType=post` and `targetId` SHALL continue to behave as specified in `forum-moderation-post-ops` and Issue #22; this change SHALL NOT alter its required parameters or post-thread aggregation semantics.

#### Scenario: Post detail audit still requires post id

- **WHEN** a moderator calls `GET /api/mod/audit?targetType=post&targetId={postId}`
- **THEN** the response SHALL follow the per-post thread audit rules without requiring the feed endpoint

### Requirement: Feed query index supports time-ordered scans

The system SHALL maintain a MongoDB index on `forum_moderation_audit` suitable for time-descending feed queries filtered by `metadata.boardId`, created during application startup in `ForumMongoSetup`, without removing the existing `(targetType, targetId, occurredAtUtc)` index.

#### Scenario: Index exists after startup

- **WHEN** the Model API starts against an empty database
- **THEN** the feed-oriented index SHALL be created alongside existing audit indexes

