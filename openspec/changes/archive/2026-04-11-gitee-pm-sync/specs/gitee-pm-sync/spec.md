## ADDED Requirements

### Requirement: Gitee sync uses API v5 with private token

The sync tooling SHALL call only `https://gitee.com/api/v5` endpoints documented for repository milestones, issues, and labels, and SHALL authenticate using a private token supplied via environment variable (never committed).

#### Scenario: Missing token

- **WHEN** the sync runs without a configured token
- **THEN** it SHALL exit with a clear error and SHALL NOT send authenticated requests

### Requirement: Milestone idempotent alignment

The sync tooling SHALL ensure milestones named in the input exist on the target repository by listing existing milestones and creating any missing titles before associating issues.

#### Scenario: Second run

- **WHEN** the sync runs twice with the same milestone titles
- **THEN** it SHALL not create duplicate milestones with the same title

### Requirement: Issue creation and update with labels

The sync tooling SHALL create issues from the input list when no matching issue exists, and SHALL update existing issues when the matching key is found, applying labels for priority (e.g. P0, P1, P2) and optional module names as defined in the input.

#### Scenario: Issue linked to milestone

- **WHEN** an input item specifies a milestone title that exists on the repository
- **THEN** the created or updated issue SHALL be associated with that milestone

### Requirement: Labels ensured before use

The sync tooling SHALL create missing labels on the repository when the input references labels that do not exist, or SHALL document a one-time manual label setup as a prerequisite in the same change’s tasks (implementation choice).

#### Scenario: Priority label applied

- **WHEN** an issue requires label `P0` and that label does not exist
- **THEN** the tooling SHALL either create `P0` or fail with instructions to create it manually

### Requirement: No board column API dependency

The sync tooling SHALL NOT require Gitee project board or kanban column APIs for success; tracking of columns MAY use labels only (e.g. status labels) when implemented.

#### Scenario: Personal edition repository

- **WHEN** the target is a personal Gitee repository without board APIs
- **THEN** milestone and issue sync SHALL still complete successfully
