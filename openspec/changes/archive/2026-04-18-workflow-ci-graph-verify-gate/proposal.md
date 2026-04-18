## Why

`pm-plan` Issue「工作流程：CI 与可选 Git hooks 闭环」要求合并前对 `graph:verify` 有明确触发与失败语义；仅靠本地约定无法保证主分支质量。需在仓库内固化 CI 行为，并与平台「必过检查」可对接。

## What Changes

- GitHub Actions 工作流对 **全部 PR** 运行 `scripts/repo-knowledge-router` 的 `npm run verify`（与根目录 `graph:verify` 一致），避免路径过滤导致检查未运行、无法设为 Required。
- 在变更说明中约定：**默认分支**（当前 `master`）在 GitHub 上启用 **Required status checks**，将 `repo-graph-verify` 工作流中的 **`verify` 作业**设为必过。
- 可选：若仓库存在 `.githooks/pre-commit`，在 `repo-ci-graph-verify` 规格或文档中写明启用方式与失败时终止提交流程（不替代 CI）。

## Capabilities

### New Capabilities

- `repo-ci-graph-verify`：定义 CI 对图谱校验的触发范围、与 `graph:verify` 的对应关系，以及可选本地 Git hooks 的约定边界。

### Modified Capabilities

- （无）既有 `repo-knowledge-router`、`github-pm-sync` 等行为不变；本变更只增加「如何跑校验、如何门禁」的规格层描述。

## Impact

- `.github/workflows/repo-graph-verify.yml`
- 维护者需在 GitHub **Branches → Branch protection** 中勾选必过检查（一次性人工配置）
- 可选：`.githooks/` 与贡献说明（若后续补充）
