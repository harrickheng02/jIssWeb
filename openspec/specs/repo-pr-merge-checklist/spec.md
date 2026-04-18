# repo-pr-merge-checklist Specification

## Purpose

约定 Pull Request 描述模板的路径与内容，使变更与 pm-plan、OpenSpec 在 PR 描述中可对齐（**人工**审阅；检查项写在模板内，不依赖额外 Markdown 文件）。

## Requirements

### Requirement: Pull request template file

The repository SHALL include a pull request template file at `.github/pull_request_template.md`. The template SHALL prompt the author in plain Markdown (checkboxes or short sections) for: linked pm-plan Issue when applicable; OpenSpec change directory or archived change reference when applicable; merge self-check items (scope, self-test, CI status); optional cross-cutting test notes.

#### Scenario: Template is discoverable by GitHub

- **WHEN** a contributor opens a new pull request in the GitHub web UI
- **THEN** the description field SHALL be pre-filled with the contents of `.github/pull_request_template.md`

### Requirement: Reviewer guidance in template

The pull request template SHALL include a short subsection for maintainers (merge policy reminders) without requiring a separate repository document path.

#### Scenario: Author finds self-check in one place

- **WHEN** the template is viewed on GitHub
- **THEN** the author SHALL find actionable checkboxes for merge readiness in the same file as association fields
