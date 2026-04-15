---
name: pm-plan
description: >-
  分析、对照与维护 scripts/github-sync/pm-plan.yaml（远端为 GitHub 时优先；亦支持 Gitee origin）；在仓库根代执行 npm（pm:pull、graph:refresh、pm:publish 等）。用户提到 pm 规划、里程碑、Issue、pm-plan、同步 GitHub、归档后回写规划时启用。
license: MIT
metadata:
  author: project
  version: "1.4.2"
---

# pm-plan 分析维护

## 默认执行约定

- 在**仓库根**用 **`npm run …`**（由 Agent 跑终端）；不依赖仓库根 `.bat`。
- **先同步再改**：用户未声明「已 pull / 已与远端对齐」时，**先** `npm run pm:pull`；失败则说明原因并停止。不在「是否要先 pull」上反复追问。
- 缺 `scripts/github-sync` 或 `repo-knowledge-router` 依赖时执行 **`npm run pm:ci`**。对话与 Git 中勿出现 `GITHUB_TOKEN`/`GH_TOKEN`、`.env` 内容。

## 命令速查（仓库根）

| 目的 | 命令 |
|------|------|
| 拉远端 + 写回 `pm-plan.yaml` + 图册校验与产物 | `npm run pm:pull` |
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
- 同目录下 `PM_OPEN_ISSUES.md`、`.last-route.txt`（生成物，已 gitignore）
- `.cursor/rules/pm-plan.mdc`
