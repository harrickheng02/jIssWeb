## Context

仓库根 `package.json` 的 `graph:verify` 委托 `scripts/repo-knowledge-router` 的 `verify`。已有工作流 `repo-graph-verify`，此前对 `pull_request` 使用路径过滤，部分 PR 不运行该工作流，无法在 GitHub 上将对应检查设为必过而不出现「检查未出现」或策略歧义。

## Goals / Non-Goals

**Goals:**

- 每个针对默认合并目标的 PR 均在 GitHub Actions 中执行与本地 `npm run graph:verify` 等价的校验。
- 工作流产出稳定、可识别的检查名称，供 **Require status checks** 勾选。
- 规格层写清：平台强制门禁依赖仓库设置；本地 hooks 为可选增强。

**Non-Goals:**

- 用全量 E2E 替代本工作流；在 Cursor 云端强制策略。
- 自动通过 API 修改 GitHub 分支保护（需管理员权限与凭据，不纳入本仓库自动化）。

## Decisions

| 决策 | 选择 | 备选 | 理由 |
|------|------|------|------|
| PR 触发 | `pull_request` 不带 `paths` | 保留路径过滤 | 必过检查必须在每个 PR 上出现；跳过时无法作为统一门禁 |
| 推送触发 | 保留对 `openspec/**`、`pm-plan.yaml`、`.cursor/rules/**`、`repo-knowledge-router/**` 的路径过滤 | 每次 push 都跑 | 降低默认分支上无关推送的重复运行；PR 已覆盖合入前校验 |
| 作业 ID | 维持 `verify` | 改名 | GitHub 上检查名称为 `repo-graph-verify / verify`，改名会破坏已选必过项 |
| 门禁 | 文档约定 + 人工在 GitHub 勾选 Required | 仅文档 | Issue 验收要求「阻断合并」；平台强制需 Required checks |

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| PR 数量多、校验耗时增加 | `verify` 仅 Node 图谱脚本，体量可控；必要时再拆缓存或并发策略 |
| 维护者未打开分支保护 | 在 proposal/tasks 中列为显式交付项；合并前核对 |
| 默认分支从 `master` 重命名 | 更新分支保护规则中的分支名与工作流 `push` 分支（若将来收窄） |

## Migration Plan

1. 合并含工作流 YAML 变更的 PR。
2. 在 GitHub **Settings → Branches** 为默认分支添加/编辑保护规则，勾选 **Require status checks**，选中 **`verify`**（或 **`repo-graph-verify / verify`**）。
3. 故意引入图谱校验失败（PR 或 push），确认必过检查失败；恢复后可通过。

回滚：还原工作流 YAML；在分支保护中取消该必过项。

## Local hooks（可选）

仓库根执行 `git config core.hooksPath .githooks` 启用；`.githooks/pre-commit` 内失败时进程非零退出，提交中止。合入仍以 CI 的 `verify` 为准。

## Open Questions

- 是否在 `push` 到默认分支时也取消路径过滤（与主线每次提交完全一致）——当前以 PR 门禁为主，可后续再收紧。
