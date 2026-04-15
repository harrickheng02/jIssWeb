# repo-knowledge-router Specification

## Purpose

本地只读索引 `pm-plan` / OpenSpec / Cursor 规则，生成 `graph.json`、校验 `openspec/` 引用、提供 `route` 与进行中 Issue 索引（`PM_OPEN_ISSUES.md`）。

## Requirements
### Requirement: Authoritative indexing sources

The repo knowledge router tooling SHALL index only repository paths under version control: `openspec/specs/**/*.md`, `openspec/changes/**/*.md`, `scripts/github-sync/pm-plan.yaml`, and `.cursor/rules/**/*.mdc` (and MAY include `.cursor/skills/**/SKILL.md` when present). It SHALL NOT require network access for indexing.

#### Scenario: Offline build succeeds

- **WHEN** `build` runs in a clean checkout without internet
- **THEN** it SHALL produce a graph artifact without calling remote APIs

### Requirement: Graph artifact schema

The tooling SHALL write a single JSON graph document containing at minimum: `version`, `generatedAt` (ISO-8601 UTC), `nodes[]` each with `id`, `kind`, `path` (repo-relative POSIX where applicable), and `edges[]` each with `from`, `to`, and `rel`. `kind` SHALL distinguish at least `spec`, `change`, `issue`, `cursor-rule`, and `module` (from pm-plan modules when represented as nodes).

#### Scenario: Round-trip stability

- **WHEN** `build` runs twice on the same sources without file changes
- **THEN** the graph document SHALL be byte-identical or differ only in `generatedAt` as explicitly documented by the implementation

### Requirement: Explicit reference edges from pm-plan

The tooling SHALL parse `scripts/github-sync/pm-plan.yaml` issue bodies and SHALL create `references` edges from each issue node to every distinct `openspec/` path string found in that issue's `body` when the referenced path exists in the workspace.

#### Scenario: Missing referenced spec

- **WHEN** an issue body references `openspec/specs/does-not-exist/spec.md`
- **THEN** `verify` (when invoked) SHALL report the missing path and non-zero exit, and `build` MAY omit the edge or attach a `brokenReference` flag on the edge as documented in the implementation

### Requirement: Route CLI output

The tooling SHALL provide a `route` command accepting a non-empty query string and SHALL print a deterministic ordered list of repo-relative paths (cap configurable, default at least 5 and at most 25) with a one-line rationale per path derived from match source (title, path token, or graph hop).

#### Scenario: Empty query rejected

- **WHEN** `route` is invoked with an empty query
- **THEN** it SHALL exit non-zero and SHALL print a usage message

### Requirement: refresh command validates before write

The tooling SHALL provide a `refresh` subcommand. It SHALL build the graph once in memory, SHALL apply the same broken-reference detection as `verify`, and SHALL exit non-zero without writing `scripts/repo-knowledge-router/data/graph.json` or `scripts/github-sync/PM_OPEN_ISSUES.md` when any broken reference exists. When no broken reference exists, it SHALL write `graph.json` and SHALL regenerate `scripts/github-sync/PM_OPEN_ISSUES.md` using the same logic as `build`.

#### Scenario: refresh aborts on broken pm-plan references

- **WHEN** `refresh` runs and an issue `body` contains an `openspec/` path that does not exist in the workspace
- **THEN** it SHALL exit non-zero and SHALL NOT update `graph.json` or `PM_OPEN_ISSUES.md`

#### Scenario: refresh writes artifacts when valid

- **WHEN** `refresh` runs and no broken reference edges exist
- **THEN** it SHALL write `graph.json` and SHALL write `PM_OPEN_ISSUES.md`

### Requirement: PM_OPEN_ISSUES index generation

During `build` and during successful `refresh`, the tooling SHALL write `scripts/github-sync/PM_OPEN_ISSUES.md`. It SHALL include issues from `pm-plan.yaml` whose `state` is `open` or `progressing` in the summary table and per-issue sections, SHALL list distinct `openspec/` path strings extracted from each included issue's `body`, and SHALL include a ranked path list per issue computed with the same scoring rules as `route` when invoked with that issue's title (result cap as implemented in code).

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

