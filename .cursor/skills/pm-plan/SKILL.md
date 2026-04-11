---
name: pm-plan
description: >-
  分析、对照与维护 scripts/gitee-sync/pm-plan.yaml（对照规范与代码，修订 YAML）。用户提到 pm 规划、里程碑、Issue、pm-plan 时启用。
license: MIT
metadata:
  author: project
  version: "1.3.0"
---

# pm-plan 分析维护

## 分析前（Plan 模式）

**先切换到 Plan 模式**，向用户确认：**是否先执行 `/pm-pull` 拉取最新 `pm-plan.yaml`（与远端对齐）**。

- 「是」→ 再分析与改稿（由用户或终端执行该命令）。
- 「否」→ 以当前磁盘上的 `scripts/gitee-sync/pm-plan.yaml` 为基线。

## 分析什么

- `pm-plan.yaml` 与 **OpenSpec**（`openspec/specs/**`、相关 `openspec/changes/**`）是否一致
- 与 **仓库实现**（服务、路由、可核验行为）是否一致
- 里程碑、Issue 的优先级、模块、状态、**`body` 是否可独立评审**

细则见 **`.cursor/rules/pm-plan.mdc`**；编辑 Issue 时 **`body`** 必须符合该文件对 **`body`** 的约定。

## 输出与修改

- 先做**对比结论**（差异、建议），再按用户意图**编辑** `scripts/gitee-sync/pm-plan.yaml`。
- Issue **交付/完结**时改 **`state`** 为 **`closed`**（或 **`rejected`**），**不要删除**该条；细则见 **`pm-plan.mdc`**。
- **`scripts/gitee-sync/.env`** 勿写入对话或 Git。

## 禁止

- 虚构规范或代码中不存在的功能
- 把密钥写入 `pm-plan.yaml`

## 相关文件

- `scripts/gitee-sync/pm-plan.yaml`
- `.cursor/rules/pm-plan.mdc`
