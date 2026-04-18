## Why

pm-plan Issue 11（工作流程：变更审阅与合并门禁清单）要求可勾选的 PR/合并检查单、与 `graph:verify`/OpenSpec 的显式关联，以及可文档化的「未过门禁不合并」实践。当前 CI 已有 `repo-graph-verify`，但缺少统一 PR 模板与独立说明文档，审阅者难以按同一清单验收。

## What Changes

- 新增 **GitHub PR 模板**（`.github/pull_request_template.md`），含 pm-plan Issue、OpenSpec 变更/归档引用、`graph:verify` 与 CI 说明等可勾选或必填小节。
- 新增 **独立 Markdown 检查单**（`docs/engineering/pr-merge-checklist.md`），与模板对齐并写清合并门禁与人工演示/记录方式（不替代分支保护配置本身）。
- **不**修改业务代码与现有 workflow 行为；**不**引入强制商业审批流。

## Capabilities

### New Capabilities

- `repo-pr-merge-checklist`: 约定 PR 模板路径、检查单文档路径及须覆盖的审阅项（与 Issue 11 验收对齐）。

### Modified Capabilities

- （无）

## Impact

- `.github/`、`docs/engineering/` 新增文件；合并后归档时写入 `openspec/specs/repo-pr-merge-checklist/spec.md`。
- 贡献者打开 PR 时默认看到模板；维护者关闭 Issue 11 时可引用上述路径。
