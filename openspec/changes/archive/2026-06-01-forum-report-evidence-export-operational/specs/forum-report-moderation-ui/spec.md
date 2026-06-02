## ADDED Requirements

### Requirement: Report queue exposes evidence export for closed reports only

The report queue view on **`/moderation/reports`** SHALL expose an **导出证据包** control for report rows whose canonical **`status`** is **`resolved`** or **`rejected`**. The control SHALL call **`GET /api/mod/reports/{reportId}/evidence`** and trigger a browser download of the returned zip archive. The control SHALL NOT be offered for **`pending`** rows (including rows that have been acknowledged but remain pending). When export is unavailable because the report is not closed, the UI SHALL omit the control or show it disabled with user-visible guidance that export is available after the report is closed. The primary export action SHALL use **`el-button type="primary"`** and styling SHALL use **`forum-tokens.css`** variables consistent with other moderation actions.

#### Scenario: Moderator exports evidence for closed report

- **WHEN** a moderator views a report row with canonical status `resolved` or `rejected`
- **THEN** an export control SHALL be visible
- **WHEN** the moderator activates export
- **THEN** the client SHALL request `GET /api/mod/reports/{reportId}/evidence` with blob response handling
- **AND** the browser SHALL download a zip file

#### Scenario: Pending report has no export control

- **WHEN** a report row has canonical status `pending`
- **THEN** the export evidence control SHALL NOT be enabled for download

#### Scenario: Export error surfaces user-visible feedback

- **WHEN** export returns HTTP 400 with code `REPORT_NOT_CLOSED` or HTTP 404 with code `EVIDENCE_EXPIRED` or `REPORT_NOT_FOUND`
- **THEN** the UI SHALL show an explicit error message
