# repo-pr-merge-checklist Specification

## Purpose

约定仓库内 Pull Request 描述模板与独立合并审阅检查单的路径与内容，使变更与 pm-plan、OpenSpec 对齐可核验（**人工**审阅与 PR 模板）。

## Requirements

### Requirement: Pull request template file

The repository SHALL include a pull request template file at `.github/pull_request_template.md`. The template SHALL prompt the author to address the following topics in plain Markdown (checkboxes or short sections): linked pm-plan Issue when applicable; OpenSpec change directory or archived change reference when applicable; self-review against `docs/engineering/pr-merge-checklist.md`; optional note on cross-cutting tests relevant to the change.

#### Scenario: Template is discoverable by GitHub

- **WHEN** a contributor opens a new pull request in the GitHub web UI
- **THEN** the description field SHALL be pre-filled with the contents of `.github/pull_request_template.md`

### Requirement: Standalone merge checklist document

The repository SHALL include a Markdown document at `docs/engineering/pr-merge-checklist.md` that restates or expands the merge review checklist and states that merges to protected branches SHALL NOT proceed while required repository checks are failing. It SHALL NOT require a specific commercial approval workflow.

#### Scenario: Checklist documents review policy

- **WHEN** a reader opens `docs/engineering/pr-merge-checklist.md`
- **THEN** they SHALL find guidance for human merge review and CI expectations, without mandating non-repository tooling

### Requirement: Cross-reference between template and checklist

The pull request template SHALL contain a link to `docs/engineering/pr-merge-checklist.md` (repository-relative path as rendered on GitHub).

#### Scenario: Navigation from PR to full checklist

- **WHEN** the template is viewed on GitHub
- **THEN** it SHALL include a clickable relative link to the engineering checklist document
