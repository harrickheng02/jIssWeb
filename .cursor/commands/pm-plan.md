---
name: /pm-plan
id: pm-plan
category: Workflow
description: 对照规范与工程修订 pm-plan.yaml；npm 与自动化约定见 pm-plan skill
---

遵循 **`.cursor/skills/pm-plan/SKILL.md`** 与 **`.cursor/rules/pm-plan.mdc`**。

**Agent**：按 skill 在仓库根执行 `npm run pm:pull` / `npm run pm:push` 等，**少问多做**；读/写 `scripts/github-sync/pm-plan.yaml`；对照 `openspec/**` 与代码。

**不做**：编造能力；把密钥写入 YAML。
