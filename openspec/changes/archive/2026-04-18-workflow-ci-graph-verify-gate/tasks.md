## 1. CI 工作流

- [x] 1.1 确认 `.github/workflows/repo-graph-verify.yml` 中 `pull_request` 无 `paths` 过滤，且 `verify` 作业执行 `npm run verify --prefix scripts/repo-knowledge-router`

## 2. 平台门禁

- [x] 2.1 在 GitHub 默认分支（`master`）上配置分支保护：启用 **Require status checks**，必选 **`repo-graph-verify / verify`**（或界面中的 **`verify`**）

## 3. 验收

- [x] 3.1 故意引入图谱校验失败（PR 或 push），确认 CI/必过检查失败且恢复后可通过

## 4. 可选本地 hooks

- [x] 4.1 若保留 `.githooks/pre-commit`：在团队可见的一处（如已有贡献说明或本变更 `design.md` 已足够）确认「启用 `core.hooksPath`、失败即终止提交」可被新人找到；无单独文档亦可接受
