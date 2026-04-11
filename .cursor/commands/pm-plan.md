---
name: /pm-plan
id: pm-plan
category: Workflow
description: 对照规范与工程修订 pm-plan.yaml；分析前 Plan 模式询问是否先 /pm-pull
---

遵循 **`.cursor/skills/pm-plan/SKILL.md`** 与 **`.cursor/rules/pm-plan.mdc`**。

**分析前**：**Plan 模式**下询问是否先 **`/pm-pull`**；再对照规范与代码，修订 YAML（含 Issue **`body`** 须满足 mdc）。

**不做**：编造能力；把密钥写入 YAML。

**可做**：读/写 `scripts/gitee-sync/pm-plan.yaml`；对照 `openspec/**` 与代码。
