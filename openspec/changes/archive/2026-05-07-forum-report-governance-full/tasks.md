## 1. Model.Api — 删除能力（与举报状态解耦）

- [x] `ForumModerationDeleteService`：`TryDeletePostAsync` / `TryDeleteReplyAsync`，与 **`forum-moderation-delete-content`** 一致。
- [x] `DELETE /api/mod/posts/{postId}`、`DELETE /api/mod/replies/{replyId}` 与审计 `post.modDelete` / `reply.modDelete`。
- [x] 集成测试覆盖删帖删回复路径（例如 `ModerationDeleteTests`、`ForumReportTests` 中与报表共存的回归）。

## 2. Model.Api — 举报工作流（三态 PATCH）

- [x] `PATCH /api/mod/reports/{id}` 受理 **`{ "status": ... }`**：`pending`、`rejected`、`resolved`（及 `dismissed`→`rejected`、`acknowledged`→`resolved`）；**任意当前状态可再 PATCH**。
- [x] **`PATCH` 成功仅更新 `forum_reports`**，**不写入** **`forum_moderation_audit`** 中举报状态类审计（与根 **`openspec/specs/forum-report-api`** 一致；内容类操作仍经 **`DELETE`** 写 `post.modDelete` / `reply.modDelete`）。
- [x] 列表 DTO 不含 **`resolutionCode`**；状态更新路径清空 **`ResolutionCode`**。**删帖 / 删回复** 仅经 **`DELETE`** 端点，不经举报 **`PATCH`** 编排。
- [x] **`forum_reports`** 已结案单据按 **`Forum:ReportRetention`**（`ClosedRetentionDays`、定时 **`BackgroundService`**）硬删过期行；**`forum_moderation_audit`** 不因该 Job 删除。开发环境 **`ReportRetention.Enabled=false`、`StartupDelayMinutes=0`**。

## 3. Frontend

- [x] 举报队列：**默认只看待处理**（`GET` 传 **`status=pending`**）；「全部状态」时不传 **`status`**；其余筛选与行内 **`PATCH`**（仅 `status`）与实现对齐。
- [x] `clients.ts`：`ForumReportModStatus`、`patchModerationForumReportStatus`。
- [x] 队列页请求与展开预览逻辑集中在 **`frontend/src/composables/useModerationReportsQueue.ts`**，`ModerationReportsQueueView.vue` 负责布局与展示。

## 4. OpenSpec

- [x] 根规范 **`openspec/specs/forum-report-api`**、**`forum-report-moderation-ui`** 与 **`forum-moderation-delete-content`** 与实现对齐。
- [x] **归档**：本目录位于 **`openspec/changes/archive/2026-05-07-forum-report-governance-full/`**，内含 **`manual-qa.md`**；举报全链路步骤可交叉引用 **`2026-05-07-forum-report-minimal-closed-loop/manual-qa.md`**。

## 5. 后续（另开变更 / Issue #18）

- [ ] 账号处罚与 user-service；SLA / 指派；举报人通知；证据导出。
