## ADDED Requirements

### Requirement: Report queue exposes warning and mute controls with required reason

The expanded report-queue row (or equivalent governance panel on `/moderation/reports`) SHALL expose controls to issue a **warning** or **mute** against the reported content author. The mute control SHALL offer preset durations **24 hours (default selected)**, **7 days**, and **30 days**. A **reason** text field SHALL be required before submit; the submit affordance SHALL remain disabled until `reason` is non-empty after trim. Successful calls SHALL use `POST /api/mod/users/{sub}/sanctions` or `POST /api/mod/reports/{reportId}/sanctions` with `reportId` set to the current queue item, `type`, `durationPreset` when muting, and `reason`. The UI SHALL surface `403 FORUM_MUTED` feedback only on the author's own compose surfaces (not on the moderation panel).

#### Scenario: Default mute duration is twenty-four hours

- **WHEN** a moderator opens the mute dialog from a report row
- **THEN** the duration selector SHALL default to twenty-four hours

#### Scenario: Submit disabled without reason

- **WHEN** the reason field is empty
- **THEN** warn and mute submit buttons SHALL be disabled

#### Scenario: Delete from queue sends reportId only

- **WHEN** a moderator deletes post or reply content from the expanded report row
- **THEN** the client SHALL call the moderation delete endpoint with `reportId` in the request body
- **AND** the client SHALL NOT prompt for a delete reason or notify the content author

### Requirement: Muted member sees compose blocking feedback

When a muted member attempts a blocked write from post compose or reply UI, the frontend SHALL display user-visible messaging derived from `FORUM_MUTED`, including localized `mutedUntilUtc` when returned by the API.

#### Scenario: Compose shows mute message

- **WHEN** a muted user submits a post and receives `403` with code `FORUM_MUTED`
- **THEN** the UI SHALL show that posting is restricted until the indicated time
