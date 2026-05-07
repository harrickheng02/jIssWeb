## Context

本变更补齐版主 **硬删除帖子 / 回复**（`ForumModerationDeleteService`、`DELETE` 端点、审计 **`post.modDelete`** / **`reply.modDelete`**），并与举报数据域共存。

提案期曾设想「举报结案 = **`resolutionCode`** + PATCH 内编排删除」。**当前仓库收敛结果**（与 **`openspec/specs/**`、根 `tasks` 一致）：

- **举报结案**：**`PATCH /api/mod/reports/{id}`** **仅** **`{ "status": ... }`** — **`pending` \| `rejected` \| `resolved`**（含 `dismissed`/`acknowledged` 别名），**任意状态可再 PATCH**；成功响应仅持久化 **`forum_reports`**（根 **`forum-report-api`**）。
- **删内容**：经 **`forum-moderation-delete-content`** 暴露的 **`DELETE`** 端点完成（帖子详情与举报队列展开区共用）；举报 **`PATCH`** 专职写入工作流 **`status`**。
- **事由码**：对外 **`GET`/`PATCH`** 契约以 **`pending` / `rejected` / `resolved`** 为主；若库内仍存 **`ResolutionCode`** 字段，状态 **`PATCH`** 路径将其清空以保持与三态界面一致。

下文 **`Historical`** 保留原proposal笔触便于归档研读；实现校验请以 **`openspec/specs/forum-report-api`**、**`forum-report-governance-full/tasks.md`** 勾选条目为准。

---

### Historical（提案草稿）

论坛举报已写入 `forum_reports`；帖文与回复需有可审计删除路径后方可与安全结案语义对齐。

**原计划 Goals：** **`resolutionCode`** 区分事由、`resolutionCode` 驱动 PATCH（含删除类码在先删后结的编排）。

**原计划 Non-Goals：** 账号冻结、指派 SLA、`forum-moderation-delete-content` 之外的处罚链路。

**原计划 Decisions（部分已由演进替代）：**

- **`resolutionCode`** 枚举：`reject_*`、`resolved_*`、`action_delete_*`
- **PATCH** body：**`resolutionCode`**；删除类与 **`ForumModerationDeleteService`** 同链路校验 **`targetType`**

---

### Delivered（与实现对齐）

- **`ForumModerationDeleteService`**：`TryDeletePostAsync` / `TryDeleteReplyAsync`（参与度量、回复、站内通知收敛）。
- **`DELETE /api/mod/posts/{postId}`**、**`DELETE /api/mod/replies/{replyId}`**，成功后 **`post.modDelete`** / **`reply.modDelete`** 审计。
- **`forum-moderation-delete-content`** 根规范落地见 **`openspec/specs/forum-moderation-delete-content`**。
- **`forum_reports`** 已结案过期清理：**`ForumReportRetentionPurger`** + **`ForumReportRetentionPurgeHostedService`**，配置 **`Forum:ReportRetention`**。

- 删帖不可逆 → 仅 mod/admin；每条删除有审计；后续可加软删字段。
- 举报人结案通知、账号处罚仍走后续 Issue / 专项。

## Open Questions（仍适用）

- 举报人站内通知（结案摘要）；导出包 — 建议下一专项。
