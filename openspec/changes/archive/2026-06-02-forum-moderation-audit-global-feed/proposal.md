## Why

Issue #18 后续项：运营反馈「找记录难」——现有审计仅能在帖子详情按 `postId` 查看，无法按时间/版区/操作类型横切检索。举报队列是工单视角，补不齐全局处置时间线。在 #22 帖线程审计与 #19 单举报证据 zip 已交付后，现可安全新增独立 feed 与 CSV 归档，且不改既有按帖 API。

## What Changes

- 新增 `GET /api/mod/audit/feed`：时间、`action[]`、可选 `boardId`；管理员默认全站，版主默认限定 `forumBoardIds`，UI 可切「全部可见版区」（仍不越 JWT 范围）。
- 新增 `GET /api/mod/audit/export`：与 feed 相同筛选，返回 UTF-8 CSV；单次行数上限；成功写 `audit.export` 审计行。
- Mongo 为 feed 查询增加 `occurredAtUtc` 导向索引；列表 DTO 含展示用 label、版区、可跳转 `postId`。
- 前端：`/moderation/audit`「审计动态」页；与举报队列 Tab 切换；筛选与导出按钮；遵循 `forum-tokens.css`。

## Capabilities

### New Capabilities

- `forum-moderation-audit-feed`：全局审计 feed 查询、版主/管理员默认范围、CSV 导出与 `audit.export` 留痕。

### Modified Capabilities

- `forum-moderation-sticky-ui`：治理区 Tab/路由（举报队列 ↔ 审计动态）；feed 筛选与导出 UI 契约。

## Impact

- **JIssWeb.Model.Api**：新 controller 方法或 `ModAuditController` 扩展、`AuditFeedQuery`、索引、CSV 生成；集成测试。
- **Frontend**：`router`、`ModerationReportsQueueView` 或共享 Tab 条、新 `ModerationAuditFeedView`、`clients.ts`、Vitest。
- **依赖**：#22 动作码与 `metadata.boardId`/`postId` 契约；`ForumModerationAccessService`、`token-identity-consistency`。
- **pm-plan**：Issue #18「后续方向」首条可标为本 change。

## 非目标

`operatorSub` 与帖子标题关键词检索；批量打包多份 report evidence zip；侧栏式治理 dashboard / 多模块运营大屏；修改按帖 `GET /api/mod/audit` 必填 `targetId` 契约；版区跨 JWT 的全站版主视图；异步导出任务队列。

**已纳入本 change（相对原 proposal 增量）**：统一治理 Tab 工作台（`/moderation` 默认审计动态，含举报队列与标签管理 Tab），见 `forum-moderation-sticky-ui` delta。
