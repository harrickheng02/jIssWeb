---
name: "PM: Pull"
description: 用 GitHub API 拉 Issue 写回 pm-plan.yaml（与 git pull 不是同一命令）
category: Workflow
tags: [pm, github-issues, sync]
---

日常同步仓库：**`git pull`**。只有要从 **GitHub Issues API** 回填本地 `pm-plan.yaml` 时再执行下面命令（由 Agent 在仓库根；缺依赖先 `npm run pm:ci`）：

```bash
npm run pm:pull
```

等价：调用 pm-pull 写回 `scripts/github-sync/pm-plan.yaml`。

细则见 `.claude/skills/pm-plan/SKILL.md`。

勿在对话中输出 `GITHUB_TOKEN`/`GH_TOKEN` 或 `.env` 内容。完成后简要说明是否成功。
