---
name: pm-plan
description: >-
  分析、对照与维护 scripts/github-sync/pm-plan.yaml（本仓库远端为 GitHub）；在仓库根代执行 npm（graph:refresh、pm:publish 等）。用户提到 pm 规划、里程碑、Issue、pm-plan、归档后回写规划时启用。
license: MIT
metadata:
  author: project
  version: "1.4.5"
---

# pm-plan 分析维护

## 端到端工作流（参考）

1. 在 **GitHub** 维护 Issue、里程碑（与团队约定一致即可）。
2. **前置**：本机 **openspec** CLI；仓库根 **`npm run pm:ci`**（首次或缺依赖）。动手前 **`git pull`**；多人改 **`pm-plan.yaml`** 前先拉再改。`npm run pm:pull` / `npm run pm:push` 需 **`scripts/github-sync/.env`**（勿泄露 token）。
3. 需把 **Issues API** 回填进 **`pm-plan.yaml`** 并刷新图册：**`/pm-pull`** 或 **`npm run pm:pull`**（内含 **`graph:refresh`**）。**勿与 `git pull` 混淆。**
4. **`scripts/github-sync/PM_OPEN_ISSUES.md`** 查看进行中 Issue（**`pm:pull` / `graph:refresh`** 生成；已 gitignore）。
5. **`/opsx-explore`** 讨论方案（**不写业务实现**）；**`/opsx-propose` → `/opsx-apply`**。**一条 Issue 可多个 change/PR**。
6. 自测；**`change-review`**（审查≠CI）；**`git commit`**，**PR**；**建议 CI 通过后再 `/opsx-archive`**。
7. **`/opsx-archive`**。
8. 对照归档与实现改 **`pm-plan.yaml`**；**`npm run pm:publish`**。**默认在实现已合并主分支后再 publish**；**勿**只 **`pm:push`** 跳过 refresh。**`publish` 失败**多因 **`graph:verify`**：检查 **`body`** 内 **`openspec/...`** 是否存在。**紧急修复**可先合代码再补规范，团队约定。

## 默认执行约定

- 在**仓库根**用 **`npm run …`**（由 Agent 跑终端）；不依赖仓库根 `.bat`。
- **先同步再改**：用户未声明已对齐时，**先 `git pull`**（与 GitHub 仓库对齐代码与已提交的 `pm-plan.yaml`）。**不要**把 `git pull` 当成 `npm run pm:pull`。
- 仅当需要从 **GitHub Issues API** 把里程碑/Issue **回填进本地** `pm-plan.yaml` 时执行 **`npm run pm:pull`**（需 `GITHUB_TOKEN`，会串联 **`graph:refresh`**）。不需要 API 回填则改完 yaml/openspec 后直接 **`npm run graph:refresh`** 即可。
- 缺 `scripts/github-sync` 或 `repo-knowledge-router` 依赖时执行 **`npm run pm:ci`**。对话与 Git 中勿出现 `GITHUB_TOKEN`/`GH_TOKEN`、`.env` 内容。

## 命令速查（仓库根）

| 目的 | 命令 |
|------|------|
| 用 GitHub API 拉 Issue → 写回 `pm-plan.yaml` + 图册产物 | `npm run pm:pull`（需 token） |
| 仅改完 yaml / openspec 后刷新图与 `PM_OPEN_ISSUES.md` | `npm run graph:refresh` |
| 路由（关键词可含空格） | `npm run graph:route -- -- "<关键词>"` |
| 改完规划后推远端（先 refresh 再 push 门禁） | `npm run pm:publish` |
| 仅校验 `body` 内 openspec 路径 | `npm run graph:verify` |

`pm:publish` = `graph:refresh` + `pm:push`（`pm:push` 内含 `graph:verify` + 远端同步；远端由 origin 或 `PM_SYNC_PROVIDER` 决定）。

**本地提交前校验（可选）**：一次性执行 `git config core.hooksPath .githooks`，则 `git commit` 前会跑 **`npm run graph:verify`**（见仓库 **`.githooks/pre-commit`**）。未配置 hooks 时不影响日常。

## 分析什么

- `pm-plan.yaml` 与 **OpenSpec**（`openspec/specs/**`、相关 `openspec/changes/**`）是否一致
- 与 **仓库实现**是否一致
- 里程碑、Issue 的优先级、模块、状态、**`body` 是否可独立评审**

细则 **`.cursor/rules/pm-plan.mdc`**；**`body`** 须含可验证路径与验收表述。

## 输出与修改

- 先做**对比结论**（差异、建议），再**编辑**解析得到的目录下的 `pm-plan.yaml`（本仓库多为 `scripts/github-sync/pm-plan.yaml`）。
- Issue **交付/完结**：**`state`** → **`closed`** 或 **`rejected`**，**不删**条目。
- **`scripts/github-sync/.env`** 勿写入对话或 Git。

## 归档后回写（与 OpenSpec 归档衔接）

1. 对照 **`openspec/changes/archive/…`** 与 **`openspec/specs/**`**。
2. **用本 skill 直接改** `pm-plan.yaml`（状态、`body` 依赖路径与验收句与远端 Issue 一致）。
3. **`npm run pm:publish`**（或先 `graph:refresh` 再单独 `pm:push` 亦可，优先一条 `pm:publish`）。

## 禁止

- 虚构规范或代码中不存在的功能
- 把密钥写入 `pm-plan.yaml`

## 相关文件

- `scripts/github-sync/pm-plan.yaml`（由 `scripts/repo-knowledge-router/src/pm-sync-dir.mjs` 解析）
- 同目录下 `PM_OPEN_ISSUES.md`（生成物，已 gitignore）
- `.cursor/rules/pm-plan.mdc`
