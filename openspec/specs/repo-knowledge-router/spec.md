# repo-knowledge-router Specification

## Purpose

本地只读索引 `pm-plan` / OpenSpec / Cursor 规则，生成 `graph.json`、校验 `openspec/` 引用、提供 `route` 与进行中 Issue 索引（`PM_OPEN_ISSUES.md`）。可选语义索引见 `repo-knowledge-semantic-index`，默认不参与 `build`/`refresh`/`verify`。

## Requirements

### Requirement: Authoritative indexing sources

The repo knowledge router tooling SHALL index only repository paths under version control: `openspec/specs/**/*.md`, `openspec/changes/**/*.md`, `scripts/github-sync/pm-plan.yaml`, and `.cursor/rules/**/*.mdc` (and MAY include `.cursor/skills/**/SKILL.md` when present). The default `build` and `refresh` operations for the JSON graph artifact SHALL NOT require network access to remote APIs. Optional semantic indexing defined in `repo-knowledge-semantic-index` SHALL NOT run as part of default `build` or `refresh`.

#### Scenario: Offline build succeeds

- **WHEN** `build` runs in a clean checkout without internet
- **THEN** it SHALL produce a graph artifact without calling remote APIs

### Requirement: Graph artifact schema

The tooling SHALL write a single JSON graph document containing at minimum: `version`, `generatedAt` (ISO-8601 UTC), `nodes[]` each with `id`, `kind`, `path` (repo-relative POSIX where applicable), and `edges[]` each with `from`, `to`, and `rel`. `kind` SHALL distinguish at least `spec`, `change`, `issue`, `cursor-rule`, and `module` (from pm-plan modules when represented as nodes).

#### Scenario: Round-trip stability

- **WHEN** `build` runs twice on the same sources without file changes
- **THEN** the graph document SHALL be byte-identical or differ only in `generatedAt` as explicitly documented by the implementation

### Requirement: Explicit reference edges from pm-plan

The tooling SHALL parse `scripts/github-sync/pm-plan.yaml` issue bodies and SHALL create `references` edges from each issue node to each distinct path string found in that issue's `body` as follows: (a) every `openspec/` path string when the referenced path exists in the workspace; (b) every `.cursor/rules/` path string ending in `.mdc` when the referenced path exists in the workspace.

#### Scenario: Missing referenced spec

- **WHEN** an issue body references `openspec/specs/does-not-exist/spec.md`
- **THEN** `verify` (when invoked) SHALL report the missing path and non-zero exit, and `build` MAY omit the edge or attach a `brokenReference` flag on the edge as documented in the implementation

#### Scenario: Optional Cursor rule path missing

- **WHEN** an issue body references a `.cursor/rules/` path that does not exist in the workspace
- **THEN** `verify` SHALL NOT exit non-zero solely for that reason; the implementation MAY omit the edge or record a non-fatal warning

### Requirement: OpenSpec reference gate for flagged issues

The tooling SHALL treat an issue as subject to the OpenSpec gate when `requires_openspec_spec_reference` is `true` on that issue object in `scripts/github-sync/pm-plan.yaml`. It SHALL NOT infer this flag from `milestone`, `module`, or remote labels. For each such issue whose `state` is `open` or `progressing`, `verify` SHALL fail with non-zero exit unless the issue `body` contains at least one `openspec/specs/` path string that resolves to an existing file.

#### Scenario: Flagged issue without resolvable OpenSpec spec path

- **WHEN** `verify` runs and a flagged issue in `open` or `progressing` state has no `openspec/specs/**` reference that resolves to an existing file
- **THEN** it SHALL exit non-zero and SHALL report the issue identifier

#### Scenario: Unflagged issue without spec path

- **WHEN** `verify` runs and `requires_openspec_spec_reference` is absent or false
- **THEN** it SHALL NOT require an `openspec/specs/**` reference in `body`

### Requirement: Route CLI output

The tooling SHALL provide a `route` command accepting a non-empty query string and SHALL print a deterministic ordered list of repo-relative paths (cap configurable, default at least 5 and at most 25) with a one-line rationale per path derived from match source (title, path token, or graph hop).

#### Scenario: Empty query rejected

- **WHEN** `route` is invoked with an empty query
- **THEN** it SHALL exit non-zero and SHALL print a usage message

### Requirement: build command validates before write

The tooling SHALL provide a `build` subcommand. It SHALL build the graph once in memory, SHALL apply the same validation as `verify` (including broken-reference detection and OpenSpec reference gate for `requires_openspec_spec_reference` issues), and SHALL exit non-zero without writing `scripts/repo-knowledge-router/data/graph.json` or `scripts/github-sync/PM_OPEN_ISSUES.md` when validation fails. When validation succeeds, it SHALL write `graph.json` and SHALL regenerate `scripts/github-sync/PM_OPEN_ISSUES.md`.

#### Scenario: build aborts when verify would fail

- **WHEN** `build` runs and validation would fail `verify`
- **THEN** it SHALL exit non-zero and SHALL NOT update `graph.json` or `PM_OPEN_ISSUES.md`

#### Scenario: build writes artifacts when valid

- **WHEN** `build` runs and validation passes
- **THEN** it SHALL write `graph.json` and SHALL write `PM_OPEN_ISSUES.md`

### Requirement: refresh command validates before write

The tooling SHALL provide a `refresh` subcommand. It SHALL build the graph once in memory, SHALL apply the same validation as `verify` (including broken-reference detection and OpenSpec reference gate for `requires_openspec_spec_reference` issues), and SHALL exit non-zero without writing `scripts/repo-knowledge-router/data/graph.json` or `scripts/github-sync/PM_OPEN_ISSUES.md` when validation fails. When validation succeeds, it SHALL write `graph.json` and SHALL regenerate `scripts/github-sync/PM_OPEN_ISSUES.md` (same outcome as successful `build`).

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

### Requirement: Workspace integration entrypoints

The root `package.json` SHALL expose npm scripts that delegate to `scripts/repo-knowledge-router` for `build`, `route`, `verify`, and `refresh` (names prefixed with `graph:` or equivalent). The package SHALL declare its Node engine constraint compatible with the repository CI image.

The root `package.json` SHALL expose `pm:pull`, `pm:push`, and `pm:publish` that compose `scripts/github-sync` with graph scripts as follows: `pm:pull` SHALL run the sync pull entrypoint and then `graph:refresh`; `pm:push` SHALL run `graph:verify` and then the sync push entrypoint; `pm:publish` SHALL run `graph:refresh` and then `pm:push`. It SHALL expose `pm:ci` that installs dependencies for both `scripts/github-sync` and `scripts/repo-knowledge-router` (for CI and fresh clones).

**Host Git:** The repository MAY ship `.githooks/pre-commit` that runs `npm run graph:verify`; hooks are not active until the user sets `git config core.hooksPath .githooks` (documented in `.cursor/skills/pm-plan/SKILL.md`).

#### Scenario: Script delegation

- **WHEN** a developer runs the documented root npm graph script
- **THEN** the correct package entry SHALL execute without requiring a global install

#### Scenario: Pull then refresh

- **WHEN** `pm:pull` completes successfully
- **THEN** it SHALL have run `graph:refresh` after the sync pull step

#### Scenario: Push gated by verify

- **WHEN** `pm:push` is invoked
- **THEN** it SHALL run `graph:verify` before the sync push step

#### Scenario: Publish chain

- **WHEN** `pm:publish` is invoked
- **THEN** it SHALL run `graph:refresh` before the same sequence as `pm:push`
