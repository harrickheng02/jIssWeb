## ADDED Requirements

### Requirement: Pull request template file

The repository SHALL include a pull request template file at `.github/pull_request_template.md`. The template SHALL prompt the author to address the following topics in plain Markdown (checkboxes or short sections): linked pm-plan Issue when applicable; OpenSpec change directory or archived change reference when applicable; confirmation that `npm run graph:verify` has been run locally or that CI will run it; optional note on cross-cutting tests relevant to the change.

#### Scenario: Template is discoverable by GitHub

- **WHEN** a contributor opens a new pull request in the GitHub web UI
- **THEN** the description field SHALL be pre-filled with the contents of `.github/pull_request_template.md`

### Requirement: Standalone merge checklist document

The repository SHALL include a Markdown document at `docs/engineering/pr-merge-checklist.md` that restates or expands the merge review checklist, references mandatory CI for `graph:verify` (workflow `repo-graph-verify`), and states that merges to protected branches SHALL NOT proceed while required checks are failing. It SHALL NOT require a specific commercial approval workflow.

#### Scenario: Checklist documents gate policy

- **WHEN** a reader opens `docs/engineering/pr-merge-checklist.md`
- **THEN** they SHALL find explicit linkage between local/CI `graph:verify` and merge eligibility, without mandating non-repository tooling

### Requirement: Cross-reference between template and checklist

The pull request template SHALL contain a link to `docs/engineering/pr-merge-checklist.md` (repository-relative path as rendered on GitHub).

#### Scenario: Navigation from PR to full checklist

- **WHEN** the template is viewed on GitHub
- **THEN** it SHALL include a clickable relative link to the engineering checklist document
