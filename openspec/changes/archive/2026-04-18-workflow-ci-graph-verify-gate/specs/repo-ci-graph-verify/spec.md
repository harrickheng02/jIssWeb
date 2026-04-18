## ADDED Requirements

### Requirement: Pull request runs graph verification in CI

The repository SHALL run automated verification equivalent to `npm run graph:verify` at the repository root (implemented as `npm run verify` in `scripts/repo-knowledge-router`) on every pull request event that targets a branch protected for merge, via GitHub Actions workflow `repo-graph-verify`.

#### Scenario: Any pull request triggers verify

- **WHEN** a pull request is opened or updated against the default integration branch
- **THEN** the `verify` job of workflow `repo-graph-verify` executes successfully or fails the check

### Requirement: Merge gate uses platform mandatory checks

Project maintainers SHALL configure GitHub branch protection on the default branch so that the status check corresponding to job `verify` in workflow `repo-graph-verify` is required before merging, such that a failing verification blocks merging.

#### Scenario: Failed verify blocks merge

- **WHEN** the `verify` job fails on a pull request
- **THEN** merging that pull request into the protected branch is not possible until checks pass or the protection rule is changed

### Requirement: Optional local hooks remain non-authoritative

If `.githooks/pre-commit` exists, documentation MAY describe enabling `core.hooksPath` and that hook failure aborts the commit; local hooks MUST NOT be the sole definition of merge policy for `graph:verify`.

#### Scenario: CI is authoritative for merge

- **WHEN** a contributor has disabled or bypassed local git hooks
- **THEN** merge eligibility for graph verification is still determined by the CI status check on the pull request
