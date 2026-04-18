---
name: pm-plan
description: >-
  分析、对照与维护 scripts/github-sync/pm-plan.yaml（本仓库远端为 GitHub）；在仓库根代执行 npm（pm:pull、pm:publish 等）。用户提到 pm 规划、里程碑、Issue、pm-plan、归档后回写规划时启用。
license: MIT
metadata:
  author: project
  version: "1.5.0"
---

# pm-plan 分析维护

## 端到端工作流（参考）

1. 在 **GitHub** 维护 Issue、里程碑（与团队约定一致即可）。
2. **前置**：本机 **openspec** CLI；仓库根 **`npm run pm:ci`**（首次或缺依赖）。动手前 **`git pull`**；多人改 **`pm-plan.yaml`** 前先拉再改。`npm run pm:pull` / `npm run pm:push` 需 **`scripts/github-sync/.env`**（勿泄露 token）。
3. 需把 **Issues API** 回填进 **`pm-plan.yaml`**：**`/pm-pull`** 或 **`npm run pm:pull`**。**勿与 `git pull` 混淆。**
4. 查看进行中 Issue：在 **`scripts/github-sync/pm-plan.yaml`** 中筛选 `state` 为 `open` / `progressing`。
5. **`/opsx-explore`** 讨论方案（**不写业务实现**）；**`/opsx-propose` → `/opsx-apply`**。**一条 Issue 可多个 change/PR**。
6. 自测；**`change-review`**（审查≠CI）；**`git commit`**，**PR**；**建议 CI 通过后再 `/opsx-archive`**。
7. **`/opsx-archive`**。
8. 对照归档与实现改 **`pm-plan.yaml`**；**`npm run pm:publish`**。**默认在实现已合并主分支后再 publish**；**勿**只 **`pm:push`** 跳过团队约定步骤。**`publish` 失败** 时根据终端报错检查 API、yaml 与网络。

## 默认执行约定

- 在**仓库根**用 **`npm run …`**（由 Agent 跑终端）；不依赖仓库根 `.bat`。
- **先同步再改**：用户未声明已对齐时，**先 `git pull`**（与 GitHub 仓库对齐代码与已提交的 `pm-plan.yaml`）。**不要**把 `git pull` 当成 `npm run pm:pull`。
- 仅当需要从 **GitHub Issues API** 把里程碑/Issue **回填进本地** `pm-plan.yaml` 时执行 **`npm run pm:pull`**（需 `GITHUB_TOKEN`）。不需要 API 回填则直接编辑 yaml 后提交。
- 缺 `scripts/github-sync` 依赖时执行 **`npm run pm:ci`**。对话与 Git 中勿出现 `GITHUB_TOKEN`/`GH_TOKEN`、`.env` 内容。

## 命令速查（仓库根）

| 目的 | 命令 |
|------|------|
| 用 GitHub API 拉 Issue → 写回 `pm-plan.yaml` | `npm run pm:pull`（需 token） |
| 将本地规划推远端（GitHub） | `npm run pm:publish` 或 `npm run pm:push` |

`pm:publish` 与 `pm:push` 在本仓库等价（均委托 `scripts/github-sync`）；远端由 origin 或 `PM_SYNC_PROVIDER` 决定。

## 分析什么

- `pm-plan.yaml` 与 **OpenSpec**（`openspec/specs/**`、相关 `openspec/changes/**`）是否一致
- 与 **仓库实现**是否一致
- 里程碑、Issue 的优先级、模块、状态、**`body` 是否可独立评审**

## 权威文件

- `scripts/github-sync/pm-plan.yaml`（由 `scripts/github-sync/pm-sync-dir.mjs` 解析同步目录）
