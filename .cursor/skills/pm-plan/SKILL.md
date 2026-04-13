---
name: pm-plan
description: >-
  分析、对照与维护 scripts/gitee-sync/pm-plan.yaml；在仓库根代执行 npm（pm:pull、graph:refresh、pm:publish 等）。用户提到 pm 规划、里程碑、Issue、pm-plan、拉需求、推 Gitee、归档后回写规划时启用。
license: MIT
metadata:
  author: project
  version: "1.4.0"
---

# pm-plan 分析维护

## 自动化（省 token）

- **不依赖** 仓库根目录 `.bat`；一律在**仓库根**用 **`npm run …`**（由 Agent 执行终端，用户不必手敲）。
- **少问多做**：用户未明确说「已拉取 / 本地已与 Gitee 对齐」时，**先执行** `npm run pm:pull`；失败则报告并停止。**禁止**在「要不要先 pull」上反复追问。
- 缺子包依赖时执行 **`npm run pm:ci`**（勿输出 `GITEE_*`、`.env`）。

## 命令速查（仓库根）

| 目的 | 命令 |
|------|------|
| 拉远端 + 写回 `pm-plan.yaml` + 图册校验与产物 | `npm run pm:pull` |
| 仅改完 yaml / openspec 后刷新图与 `PM_OPEN_ISSUES.md` | `npm run graph:refresh` |
| 路由（关键词可含空格） | `npm run graph:route -- -- "<关键词>"` |
| 改完规划后推 Gitee（先 refresh 再 push 门禁） | `npm run pm:publish` |
| 仅校验 `body` 内 openspec 路径 | `npm run graph:verify` |

`pm:publish` = `graph:refresh` + `pm:push`（`pm:push` 内含 `graph:verify` + Gitee 同步）。

**本地提交前校验（可选）**：一次性执行 `git config core.hooksPath .githooks`，则 `git commit` 前会跑 **`npm run graph:verify`**（见仓库 **`.githooks/pre-commit`**）。未配置 hooks 时不影响日常。

## 分析什么

- `pm-plan.yaml` 与 **OpenSpec**（`openspec/specs/**`、相关 `openspec/changes/**`）是否一致
- 与 **仓库实现**是否一致
- 里程碑、Issue 的优先级、模块、状态、**`body` 是否可独立评审**

细则 **`.cursor/rules/pm-plan.mdc`**；**`body`** 须含可验证路径与验收表述。

## 输出与修改

- 先做**对比结论**（差异、建议），再**编辑** `scripts/gitee-sync/pm-plan.yaml`。
- Issue **交付/完结**：**`state`** → **`closed`** 或 **`rejected`**，**不删**条目。
- **`scripts/gitee-sync/.env`** 勿写入对话或 Git。

## 归档后回写（与 OpenSpec 归档衔接）

1. 对照 **`openspec/changes/archive/…`** 与 **`openspec/specs/**`**。
2. **用本 skill 直接改** `pm-plan.yaml`（状态、`body` 依赖路径与验收句与 Gitee 一致）。
3. **`npm run pm:publish`**（或先 `graph:refresh` 再单独 `pm:push` 亦可，优先一条 `pm:publish`）。

## 禁止

- 虚构规范或代码中不存在的功能
- 把密钥写入 `pm-plan.yaml`

## 相关文件

- `scripts/gitee-sync/pm-plan.yaml`
- `scripts/gitee-sync/PM_OPEN_ISSUES.md`（生成物，已 gitignore；供 `@` 路径或 Agent Read）
- `scripts/gitee-sync/.last-route.txt`（每次 `graph:route` 覆盖，供 `@`）
- `.cursor/rules/pm-plan.mdc`
