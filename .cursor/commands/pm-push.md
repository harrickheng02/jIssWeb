---
name: /pm-push
id: pm-push
category: Workflow
description: 将 pm-plan.yaml 同步到 Gitee（先 dry-run 再推送）
---

在**仓库根目录**用终端执行（无依赖时先 `npm run pm:ci`）：

```bash
npm run pm:push
```

勿在对话中输出 `GITEE_*` 或 `.env` 内容。完成后简要说明更新条数或错误。
