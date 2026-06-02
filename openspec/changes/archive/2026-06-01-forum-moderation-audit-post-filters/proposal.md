## Why

Issue #18 子项 #22：帖详情「操作记录」仅有全量列表与固定首页，举报结案/已受理未写审计，作者处罚（警告/禁言）不出现在帖上下文。版主处理帖子时无法按行为或时间筛选、也看不到完整处置链，审计链条不完整。

## What Changes

- 扩展 `GET /api/mod/audit`：在 `targetType=post&targetId=` 前提下增加可选 `action`、`fromUtc`、`toUtc` 与既有分页联调。
- 帖线程审计聚合纳入 `user.warn` / `user.mute` / `user.unmute`（经 `metadata.reportId` 或 `metadata.postId` 关联）。
- `ModReportsController`：`PatchStatus` 结案（resolved/rejected）与 `Acknowledge` 写审计（含 `reportId`/`postId`，幂等）。
- 前端 `ForumPostGovernancePanel` 操作记录抽屉：操作类型筛选、时间范围、分页。

## Capabilities

### New Capabilities

（无新增 capability 目录；行为增量写入既有 spec delta。）

### Modified Capabilities

- `forum-moderation-post-ops`：按帖审计查询筛选/分页；帖线程含用户处罚；举报 workflow 写审计动作码与 metadata 契约。
- `forum-moderation-sticky-ui`：操作记录抽屉筛选 UI 与 API 参数对齐。
- `forum-report-api`：结案与已受理副作用写 moderation audit（与通知并存）。
- `forum-user-sanctions`：处罚审计 metadata 须含 `reportId`/`postId` 以供帖线程关联（若尚未规范则增量）。

## Impact

- **Model.Api**：`ModAuditController`、`ModReportsController`、`ModerationAuditPresentation`、Mongo 查询；集成测试。
- **Frontend**：`clients.ts`、`ForumPostGovernancePanel.vue`；遵循 `forum-tokens.css`。
- **依赖**：Issue #7/#17/#20 closed；Issue #19 change-A/B merged；父 Issue #18。

## 非目标

不绑定帖子的全局审计 feed 页；版区跨帖筛选；审计 CSV/zip 导出；新帖子治理动作（沉帖等）；修改举报通知或处罚业务语义。
