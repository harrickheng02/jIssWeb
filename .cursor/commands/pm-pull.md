---
name: /pm-pull
id: pm-pull
category: Workflow
description: 从 GitHub 拉取并写回 pm-plan.yaml，并 graph:refresh（由 Agent 在仓库根执行 npm）
---

由 **Agent 在仓库根**执行（勿让用户手敲 bat；缺依赖先 `npm run pm:ci`）：

```bash
npm run pm:pull
```

等价：`pm-pull` 写回 `pm-plan.yaml` → **`graph:refresh`**（校验 `body` 内 `openspec/` → 写 `graph.json` 与 **`scripts/github-sync/PM_OPEN_ISSUES.md`**）。

细则与自动化约定见 **`.cursor/skills/pm-plan/SKILL.md`**。

勿在对话中输出 `GITHUB_TOKEN`/`GITEE_*` 或 `.env` 内容。完成后简要说明是否成功。
