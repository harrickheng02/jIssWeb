## ADDED Requirements

### Requirement: Authoritative indexing sources

The repo knowledge router tooling SHALL index only repository paths under version control: `openspec/specs/**/*.md`, `openspec/changes/**/*.md`, `scripts/gitee-sync/pm-plan.yaml`, and `.cursor/rules/**/*.mdc` (and MAY include `.cursor/skills/**/SKILL.md` when present). It SHALL NOT require network access for indexing.

#### Scenario: Offline build succeeds

- **WHEN** `build` runs in a clean checkout without internet
- **THEN** it SHALL produce a graph artifact without calling remote APIs

### Requirement: Graph artifact schema

The tooling SHALL write a single JSON graph document containing at minimum: `version`, `generatedAt` (ISO-8601 UTC), `nodes[]` each with `id`, `kind`, `path` (repo-relative POSIX where applicable), and `edges[]` each with `from`, `to`, and `rel`. `kind` SHALL distinguish at least `spec`, `change`, `issue`, `cursor-rule`, and `module` (from pm-plan modules when represented as nodes).

#### Scenario: Round-trip stability

- **WHEN** `build` runs twice on the same sources without file changes
- **THEN** the graph document SHALL be byte-identical or differ only in `generatedAt` as explicitly documented by the implementation

### Requirement: Explicit reference edges from pm-plan

The tooling SHALL parse `scripts/gitee-sync/pm-plan.yaml` issue bodies and SHALL create `references` edges from each issue node to every distinct `openspec/` path string found in that issue's `body` when the referenced path exists in the workspace.

#### Scenario: Missing referenced spec

- **WHEN** an issue body references `openspec/specs/does-not-exist/spec.md`
- **THEN** `verify` (when invoked) SHALL report the missing path and non-zero exit, and `build` MAY omit the edge or attach a `brokenReference` flag on the edge as documented in the implementation

### Requirement: Route CLI output

The tooling SHALL provide a `route` command accepting a non-empty query string and SHALL print a deterministic ordered list of repo-relative paths (cap configurable, default at least 5 and at most 25) with a one-line rationale per path derived from match source (title, path token, or graph hop).

#### Scenario: Empty query rejected

- **WHEN** `route` is invoked with an empty query
- **THEN** it SHALL exit non-zero and SHALL print a usage message

### Requirement: Workspace integration entrypoints

The root `package.json` SHALL expose npm scripts that delegate to `scripts/repo-knowledge-router` for `build` and `route` (names prefixed with `graph:` or equivalent documented in tasks). The package SHALL declare its Node engine constraint compatible with the repository CI image.

#### Scenario: Script delegation

- **WHEN** a developer runs the documented root npm graph script
- **THEN** the correct package entry SHALL execute without requiring a global install
