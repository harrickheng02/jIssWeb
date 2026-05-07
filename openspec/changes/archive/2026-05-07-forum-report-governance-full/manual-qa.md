## 范围

覆盖 **`forum-report-governance-full`** 中与 **删帖/删回复** 及 **举报队列三态** 的手工验收；与 **`openspec/changes/forum-report-minimal-closed-loop/manual-qa.md`**（Issue #4 举报全链路）互补：**举报提交与队列**步骤以该文档 **A/B/C** 为准，本节追加 **删除与审计**.

前置：Model.Api、版主/管理员 JWT、`forum_general`/`forum_tech` 等版区下有可删帖文与回复。

## D. 帖子详情 — 硬删除

1. 以 **admin** 或 **该版 moderator** 打开帖子详情「治理」区。
2. 执行 **删回复**：选一条回复删除；期望成功提示或列表消失；Mongo `forum_moderation_audit` 出现 **`action=reply.modDelete`**。
3. 在新帖或测试帖上执行 **删帖**；期望帖文不可再按 id 打开或返回已删语义；审计 **`action=post.modDelete`**。
4. **删操作**与举报状态独立：举报若仍存在，队列里的 **`PATCH` status** 仍可照常使用。

## E. 举报与删除并列（回归）

5. 按 minimal **manual-qa** 在三态间切换举报；核对 **`PATCH`** 仅反映到 **`forum_reports`**（根 **`forum-report-api`**）。若执行过删帖/删回复，可按 **D** 节核对 **`post.modDelete` / `reply.modDelete`**。

## 自动化

- 删除：`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj --filter "FullyQualifiedName~ModerationDeleteTests"`
- 举报闭环：`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj --filter "FullyQualifiedName~ForumReportTests"`
- 举报结案保留：**`ForumReportRetentionPurgerTests`**

```bash
dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj --filter "FullyQualifiedName~ForumReportRetentionPurgerTests"
```

## F. 生产配置（可选核对）

核对 **`Forum:ReportRetention`**：上线环境通常为 **`Enabled: true`**、`ClosedRetentionDays`（默认 120）、`IntervalHours`。**开发**配置文件关闭清理且 **`StartupDelayMinutes: 0`**。