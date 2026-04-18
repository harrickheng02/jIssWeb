# PR / 合并审阅检查单

展开说明见本仓库 Issue 11（工作流程：变更审阅与合并门禁清单）验收条款；**合入代码审查讨论不能省**，本清单只约束流程与可追溯项。

## 合并前检查（维护者与作者自检）

- [ ] **pm-plan**：若本次变更对应规划条目，已关联 `scripts/github-sync/pm-plan.yaml` 中的 Issue（或远端同编号 Issue），并在 PR 描述中写明编号或标题关键词。
- [ ] **OpenSpec**：若本次变更涉及规范或任务，已引用进行中的 `openspec/changes/<name>/`（含 `tasks.md` 状态）或已归档路径 `openspec/changes/archive/.../`。
- [ ] **`graph:verify`**：本地已执行 `npm run graph:verify` 且通过；或确认本次 diff 会触发 CI 中的同名校验且你愿意在 CI 通过后合入。
- [ ] **（可选）横切测试**：与业务无关的契约/集成项（如身份最小用例、其他仓库约定脚本）已在 PR 中说明；不作为强制门禁除非团队另行约定。

## `graph:verify` 与 CI

- 本地：仓库根 `npm run graph:verify`（委托 `scripts/repo-knowledge-router`）。
- CI：工作流 **`.github/workflows/repo-graph-verify.yml`**（`repo-graph-verify`），对 `openspec/**`、`pm-plan.yaml`、`scripts/repo-knowledge-router/**`、`.cursor/rules/**` 等路径变更会在 PR 上跑与本地等价的 `verify` 作业。

规范层面见 `openspec/specs/repo-ci-graph-verify/spec.md`、`openspec/specs/repo-knowledge-router/spec.md`。

## 分支保护与「未过门禁则不合并」

- 默认分支应在 GitHub **分支保护** 中要求 **必选状态检查**（含上述 workflow 的 `verify` 作业）；检查失败时 **不得** 合并到受保护分支。
- 这不依赖个人本机 Git hook；本地 hook 仅为辅助（见 `repo-ci-graph-verify` spec）。

**非目标**：不强制特定商业审批流；不替代 Code Review。

## 如何演示门禁生效（团队可做一次留痕）

1. 在测试分支故意引入会破坏 `graph:verify` 的 `openspec` 引用或 pm-plan 路径后开 PR。  
2. 确认 Actions 中 `repo-graph-verify` / `verify` **失败**，且受保护分支上 **无法合并**。  
3. 修复后检查通过再合并。将上述步骤记在团队 Wiki 或某次 Retro 记录即可满足「可文档化演示」类验收。
