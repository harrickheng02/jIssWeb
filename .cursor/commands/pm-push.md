---
name: /pm-push
id: pm-push
category: Workflow
description: 将 pm-plan.yaml 同步到远端（默认 GitHub；先校验 openspec 引用，再 dry-run 再推送）
---

由 **Agent 在仓库根**执行（勿用 bat；缺依赖先 `npm run pm:ci`）：

```bash
npm run pm:push
```

改完 `pm-plan.yaml` 后若要**一条命令**刷新图并推送，用 **`npm run pm:publish`**（见 **`pm-plan` skill**）。

`pm:push` 会先 **`graph:verify`**（`body` 中 `openspec/...` 须存在），失败则不推远端。

勿在对话中输出 `GITHUB_TOKEN`/`GITEE_*` 或 `.env` 内容。完成后简要说明更新条数或错误。
