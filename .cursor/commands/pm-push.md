---
name: /pm-push
id: pm-push
category: Workflow
description: 将 pm-plan.yaml 同步到远端（默认 GitHub；先 dry-run 再推送）
---

由 **Agent 在仓库根**执行（勿用 bat；缺依赖先 `npm run pm:ci`）：

```bash
npm run pm:push
```

改完 `pm-plan.yaml` 后推远端可用 **`npm run pm:publish`**（与 **`pm:push`** 等价，见 **`pm-plan` skill**）。

`pm:push` 将本地 **`pm-plan.yaml`** 与远端 Issue/里程碑对齐（见 **`scripts/github-sync`** 实现）；失败则不推远端。

勿在对话中输出 `GITHUB_TOKEN`/`GH_TOKEN` 或 `.env` 内容。完成后简要说明更新条数或错误。
