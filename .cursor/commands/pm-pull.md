---
name: /pm-pull
id: pm-pull
category: Workflow
description: 从 Gitee 拉取并写回 pm-plan.yaml（npm pm:pull）
---

在**仓库根目录**用终端执行（无 `scripts/gitee-sync/node_modules` 时先 `npm run pm:ci`）：

```bash
npm run pm:pull
```

勿在对话中输出 `GITEE_*` 或 `.env` 内容。完成后简要说明是否写入成功。
