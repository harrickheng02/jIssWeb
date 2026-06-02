# forum-report-evidence-export Specification

## Purpose

定义论坛举报结案后的运营复盘级证据导出：结案时写入 `forum_report_evidence_snapshots`、已结案举报 zip 下载、与 `Forum:ReportRetention` 同周期清理；不含法务级存证链。

## Requirements

### Requirement: Evidence snapshot is written when a report enters a terminal closed state

The system SHALL persist an evidence snapshot document in `forum_report_evidence_snapshots` when an authorized `PATCH /api/mod/reports/{reportId}` successfully transitions a report into a terminal closed canonical status (`resolved` or `rejected`, including stored legacy `acknowledged` / `dismissed` mapped to those buckets). The snapshot SHALL be written at most once per unique pair of `reportId` and `HandledAtUtc` on the report document after the transition. The snapshot SHALL include a copy of the report workflow fields needed for offline review (including `reporterSub`, `reason`, `status`, handler and acknowledge timestamps, board fields, and target references) and a copy of the reported target content at close time (post or reply title/body/author identifiers when still readable, or an explicit tombstone when the target is already deleted). The system SHALL NOT write evidence snapshots on `POST /api/mod/reports/{reportId}/acknowledge`, while the report remains `pending`, or on reopen alone. Snapshot write failure SHALL NOT cause the PATCH response to fail; failures SHALL be logged.

#### Scenario: First close writes snapshot

- **WHEN** an authorized caller successfully PATCHes a report from `pending` to `resolved`
- **THEN** exactly one evidence snapshot SHALL exist for that `reportId` and the report's `HandledAtUtc`
- **AND** the snapshot target payload SHALL reflect the post or reply content available at close time

#### Scenario: Duplicate close retry is idempotent

- **WHEN** the same terminal close is retried and `HandledAtUtc` is unchanged
- **THEN** at most one snapshot document SHALL exist for that `reportId` and `HandledAtUtc`

#### Scenario: Acknowledge does not write snapshot

- **WHEN** an authorized caller successfully acknowledges a pending report
- **THEN** no new evidence snapshot SHALL be created for that report

#### Scenario: Reopen and close again writes new snapshot

- **WHEN** a report is reopened to `pending` and later closed again with a new `HandledAtUtc`
- **THEN** a new evidence snapshot SHALL exist for the new `HandledAtUtc`

### Requirement: Moderators and admins export closed report evidence as a zip archive

The system SHALL expose `GET /api/mod/reports/{reportId}/evidence` for authenticated clients whose effective forum role is moderator or admin per `token-identity-consistency`. The handler SHALL succeed only when the report's canonical status is `resolved` or `rejected`, OR when the report row has been purged but a matching evidence snapshot still exists and the caller is authorized for the snapshot's board scope. When the report exists and its canonical status is `pending`, the response SHALL be HTTP 400 with the uniform error envelope and documented code `REPORT_NOT_CLOSED`. When neither a closed report nor a non-expired snapshot exists, the response SHALL be HTTP 404 with documented code `EVIDENCE_EXPIRED` or `REPORT_NOT_FOUND` as appropriate. On success the response SHALL be HTTP 200 with `Content-Type: application/zip` and a downloadable archive assembled by `EvidenceZipBuilder` containing at minimum UTF-8 JSON entries `manifest.json`, `report.json`, `target.json`, `thread-audit.json`, and `sanctions-summary.json`. Administrators MAY export any report in scope. Moderators SHALL succeed only when the report or snapshot `boardId` is within their moderator scope; otherwise HTTP 403.

#### Scenario: Admin exports closed report

- **WHEN** an admin calls `GET /api/mod/reports/{reportId}/evidence` for a report with canonical status `resolved`
- **THEN** the response SHALL be HTTP 200 with an `application/zip` body
- **AND** the zip SHALL include `report.json` and `target.json` consistent with the evidence snapshot

#### Scenario: Pending report export rejected

- **WHEN** a moderator calls export for a report with canonical status `pending`
- **THEN** the response SHALL be HTTP 400 with code `REPORT_NOT_CLOSED`

#### Scenario: Export after report purge uses snapshot

- **WHEN** the report document has been removed by retention purge but its evidence snapshot remains within retention
- **AND** an authorized moderator calls export for that `reportId`
- **THEN** the response SHALL be HTTP 200
- **AND** `report.json` SHALL be served from the snapshot

#### Scenario: Export forbidden out of board scope

- **WHEN** a moderator calls export for a report outside their board scope
- **THEN** the response SHALL be HTTP 403

#### Scenario: Expired evidence returns not found

- **WHEN** both the report and its evidence snapshot have been purged past the retention horizon
- **THEN** the response SHALL be HTTP 404 with code `EVIDENCE_EXPIRED`

### Requirement: Evidence zip includes audit-derived sanction summaries without User service calls

When building `sanctions-summary.json`, the system SHALL derive entries only from `forum_moderation_audit` rows whose `Metadata.reportId` equals the exported report id. Each summary item SHALL include at minimum `action`, `operatorSub`, `occurredAtUtc`, and `reason` when present in audit metadata, plus `durationPreset` for mute actions when present in metadata. The export path SHALL NOT call the User service internal sanctions API.

#### Scenario: Mute from report queue appears in sanctions summary

- **WHEN** a `user.mute` audit row exists with `Metadata.reportId` matching the exported report
- **THEN** `sanctions-summary.json` SHALL include an item with `action=user.mute` and the stored `durationPreset` and `reason`

#### Scenario: Sanctions from other reports on the same post are excluded

- **WHEN** a `user.mute` audit row exists with `Metadata.reportId` equal to a different report on the same post
- **THEN** `sanctions-summary.json` for the exported report SHALL NOT include that row

### Requirement: Evidence zip readme explains operational purpose

The zip archive SHALL include a UTF-8 `readme.txt` that explains the operational review purpose of the bundle, typical use scenarios, confidentiality constraints, a brief description of each JSON entry, and the export metadata for the current download.

#### Scenario: Readme documents purpose and file layout

- **WHEN** `EvidenceZipBuilder` produces an archive
- **THEN** `readme.txt` SHALL describe the bundle purpose and list the JSON entry names with brief meanings

### Requirement: EvidenceZipBuilder is a reusable zip assembly component

The Model service SHALL implement `EvidenceZipBuilder` as a dedicated component that accepts structured bundle input (manifest metadata, report snapshot, target snapshot, ordered audit rows, and derived sanction summaries) and returns a zip archive stream or byte array. Report evidence export and future moderation audit export features SHALL reuse this builder for zip packaging rather than duplicating compression logic in controllers.

#### Scenario: Builder produces consistent entry set

- **WHEN** `EvidenceZipBuilder` is invoked with a complete bundle input
- **THEN** the output archive SHALL contain the documented JSON entry names using UTF-8 encoding

### Requirement: Evidence snapshots expire with closed report retention

Evidence snapshot documents SHALL be hard-deleted when their stored `HandledAtUtc` is strictly older than the configured `Forum:ReportRetention:ClosedRetentionDays` horizon in UTC, using the same retention configuration as closed `forum_reports` purge. The retention job SHALL NOT delete `forum_moderation_audit` rows. Pending reports SHALL NOT have snapshots eligible for this purge rule until closed and snapshotted.

#### Scenario: Expired snapshot removed with same horizon as reports

- **WHEN** the report retention background job runs
- **AND** an evidence snapshot has `HandledAtUtc` older than `ClosedRetentionDays`
- **THEN** that snapshot document SHALL be deleted
- **AND** moderation audit rows for the same report SHALL remain
