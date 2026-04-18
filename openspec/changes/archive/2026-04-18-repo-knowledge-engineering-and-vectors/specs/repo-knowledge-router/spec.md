## MODIFIED Requirements

### Requirement: Authoritative indexing sources

The repo knowledge router tooling SHALL index only repository paths under version control: `openspec/specs/**/*.md`, `openspec/changes/**/*.md`, `scripts/github-sync/pm-plan.yaml`, and `.cursor/rules/**/*.mdc` (and MAY include `.cursor/skills/**/SKILL.md` when present). The default `build` and `refresh` operations for the JSON graph artifact SHALL NOT require network access to remote APIs. Optional semantic indexing defined in `repo-knowledge-semantic-index` SHALL NOT run as part of default `build` or `refresh`.

#### Scenario: Offline build succeeds

- **WHEN** `build` runs in a clean checkout without internet
- **THEN** it SHALL produce a graph artifact without calling remote APIs

### Requirement: Explicit reference edges from pm-plan

The tooling SHALL parse `scripts/github-sync/pm-plan.yaml` issue bodies and SHALL create `references` edges from each issue node to each distinct path string found in that issue's `body` as follows: (a) every `openspec/` path string when the referenced path exists in the workspace; (b) every `.cursor/rules/` path string ending in `.mdc` when the referenced path exists in the workspace.

#### Scenario: Missing referenced spec

- **WHEN** an issue body references `openspec/specs/does-not-exist/spec.md`
- **THEN** `verify` (when invoked) SHALL report the missing path and non-zero exit, and `build` MAY omit the edge or attach a `brokenReference` flag on the edge as documented in the implementation

#### Scenario: Optional Cursor rule path missing

- **WHEN** an issue body references a `.cursor/rules/` path that does not exist in the workspace
- **THEN** `verify` SHALL NOT exit non-zero solely for that reason; the implementation MAY omit the edge or record a non-fatal warning

### Requirement: refresh command validates before write

The tooling SHALL provide a `refresh` subcommand. It SHALL build the graph once in memory, SHALL apply the same validation as `verify` (including broken-reference detection and OpenSpec reference gate for `requires_openspec_spec_reference` issues), and SHALL exit non-zero without writing `scripts/repo-knowledge-router/data/graph.json` or `scripts/github-sync/PM_OPEN_ISSUES.md` when validation fails. When validation succeeds, it SHALL write `graph.json` and SHALL regenerate `scripts/github-sync/PM_OPEN_ISSUES.md` using the same logic as `build`.

#### Scenario: refresh aborts on broken pm-plan references

- **WHEN** `refresh` runs and an issue `body` contains an `openspec/` path that does not exist in the workspace
- **THEN** it SHALL exit non-zero and SHALL NOT update `graph.json` or `PM_OPEN_ISSUES.md`

#### Scenario: refresh writes artifacts when valid

- **WHEN** `refresh` runs and validation passes
- **THEN** it SHALL write `graph.json` and SHALL write `PM_OPEN_ISSUES.md`

### Requirement: PM_OPEN_ISSUES index generation

During `build` and during successful `refresh`, the tooling SHALL write `scripts/github-sync/PM_OPEN_ISSUES.md`. It SHALL include issues from `pm-plan.yaml` whose `state` is `open` or `progressing` in the summary table and per-issue sections. For each included issue it SHALL list distinct `openspec/` path strings extracted from that issue's `body` in a dedicated subsection; it SHALL list distinct `.cursor/rules/` path strings extracted from that issue's `body` in a separate subsection when any exist. It SHALL include a ranked path list per issue computed with the same scoring rules as `route` when invoked with that issue's title (result cap as implemented in code). For issues with `requires_openspec_spec_reference: true`, when no `openspec/specs/**` reference resolves to an existing file, the per-issue section SHALL include a visible warning line.

#### Scenario: closed issues omitted from summary table

- **WHEN** `PM_OPEN_ISSUES.md` is generated
- **THEN** issues whose `state` is `closed` or `rejected` SHALL NOT appear as rows in the leading summary table

## ADDED Requirements

### Requirement: OpenSpec reference gate for flagged issues

The tooling SHALL treat an issue as subject to the OpenSpec gate when `requires_openspec_spec_reference` is `true` on that issue object in `scripts/github-sync/pm-plan.yaml`. It SHALL NOT infer this flag from `milestone`, `module`, or remote labels. For each such issue whose `state` is `open` or `progressing`, `verify` SHALL fail with non-zero exit unless the issue `body` contains at least one `openspec/specs/` path string that resolves to an existing file.

#### Scenario: Flagged issue without resolvable OpenSpec spec path

- **WHEN** `verify` runs and a flagged issue in `open` or `progressing` state has no `openspec/specs/**` reference that resolves to an existing file
- **THEN** it SHALL exit non-zero and SHALL report the issue identifier

#### Scenario: Unflagged issue without spec path

- **WHEN** `verify` runs and `requires_openspec_spec_reference` is absent or false
- **THEN** it SHALL NOT require an `openspec/specs/**` reference in `body`
