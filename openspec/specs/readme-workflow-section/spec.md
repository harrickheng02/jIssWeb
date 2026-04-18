# readme-workflow-section Specification

## Purpose

约定仓库根 `README.md` 中「需求与 OpenSpec 工作流」一节应包含的 contributor 可见说明：Git 同步与 `pm:pull` 区分、复盘/归档后的 `pm-plan` 推送、以及 PR 模板（合并前自检）引用。

## Requirements

### Requirement: README documents contributor workflow and sync discipline

The repository root `README.md` SHALL include a section covering OpenSpec / pm-plan workflow (existing section four or successor) that states: (a) `git pull` synchronizes repository code from the remote, while `npm run pm:pull` fetches GitHub Issues into `scripts/github-sync/pm-plan.yaml`; (b) after editing `pm-plan.yaml`, contributors SHOULD use `npm run pm:publish` (or `pm:push`) when pushing plan changes to the remote as appropriate; (c) a pointer to `.github/pull_request_template.md` for PR self-check context.

#### Scenario: Reader finds pull versus pm-pull distinction

- **WHEN** a contributor reads the workflow section of `README.md`
- **THEN** they SHALL find an explicit contrast between `git pull` and `npm run pm:pull` (or `pm:pull` via npm script name)

#### Scenario: Reader finds post-archive guidance

- **WHEN** a contributor reads the workflow section of `README.md`
- **THEN** they SHALL find guidance that ties retrospectives or OpenSpec archives to committing updates and syncing `pm-plan` when needed

#### Scenario: Reader finds PR template pointer

- **WHEN** a contributor reads the workflow section of `README.md`
- **THEN** they SHALL find a reference to `.github/pull_request_template.md`
