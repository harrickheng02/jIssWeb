## Why

第一期举报闭环需与**真实治理动作**衔接：版区授权下的**硬删除**能力，以及可持续迭代的 Model.Api 契约；后续 Issue #18 在同一条治理时间线上扩展处罚与通知。

## What Changes（与当前实现对齐）

- **内容删除**：版主/管理员在授权版区内可调 **`DELETE /api/mod/posts/{postId}`**、**`DELETE /api/mod/replies/{replyId}`**（顺带清理回复、互动、相关站内通知）；审计 **`post.modDelete`** / **`reply.modDelete`**。（规范：**`openspec/specs/forum-moderation-delete-content`**）。
- **举报结案**：与 **`forum-report-api`** 一致 — **`PATCH`** **仅** **`status`**（三态 + 别名），成功时仅更新 **`forum_reports`**；列表 DTO **不含** `resolutionCode` 字段。删帖/删回复在 **帖子详情治理区** 与 **举报队列展开区** 调用同一套 **`DELETE`**，与举报状态更新分离。
- **兼容**：Mongo 存量 **`dismissed`** / **`acknowledged`** 在筛选与展示中并入 **`rejected`** / **`resolved`**。

## Capabilities

### New Capabilities

- **`forum-moderation-delete-content`**：删帖与删回复的 HTTP、鉴权、审计（与 Issue #18 演进一致）。

### Modified Capabilities

- **`forum-report-api`** / **`forum-report-moderation-ui`**：与删除能力**并列交付**；举报 PATCH 与删除 API **解耦**（契约见根 **`openspec/specs/**`**）。

### Phased（后续变更 / Issue #18）

- 账号侧：警告、禁言、封禁与 user-service。
- SLA / 指派 / 升级队列。
- 举报人回馈（结案通知）。
- 证据导出。

## Impact

**`JIssWeb.Model.Api`**、`frontend`（帖子详情治理删除 + 举报队列 **`PATCH` status**）、**`openspec/specs`**（**`forum-report-*`**、**`forum-moderation-delete-content`**）；集成测试补强。
