## Why

Issue #19 change-C：`forum_reports` 结案后按 `Forum:ReportRetention`（默认 120 天）硬删，工单字段与目标正文随之不可还原；审计虽保留但无离线证据包。版主需在结案后导出运营复盘级 zip，并与 retention 同周期清理。

## What Changes

- 结案（`resolved`/`rejected`）时写入 evidence snapshot（幂等，失败不阻断 PATCH）。
- 新增 `GET /api/mod/reports/{reportId}/evidence`：仅 closed 可导出 `application/zip`；pending 返回 400。
- 抽取 `EvidenceZipBuilder` 组装 manifest/report/target/thread-audit/sanctions-summary。
- snapshot 与 `forum_reports` 共用 `ClosedRetentionDays` 一并 purge。
- 前端举报队列：closed 行展示「导出证据包」按钮；遵循 `forum-tokens.css`。

## Capabilities

### New Capabilities

- `forum-report-evidence-export`：结案 snapshot、zip 导出、retention 协调、`EvidenceZipBuilder` 契约。

### Modified Capabilities

- `forum-report-api`：结案写 snapshot 副作用；export 端点；purge 扩展至 snapshot 集合。
- `forum-report-moderation-ui`：举报队列 closed 行导出按钮与 disabled 提示。

## Impact

- **Model.Api**：新集合、`ModReportsController`、`EvidenceZipBuilder`、purge 扩展；集成测试。
- **Frontend**：`clients.ts`、`ModerationReportsQueueView.vue`。
- **依赖**：Issue #19 change-A/B、Issue #22；与 Issue #18 审计导出并行规划边界（本 change 仅单 report）。

## 非目标

法务级哈希链/WORM；pending/acknowledge 阶段导出；User 内网 sanctions 全量；批量审计 CSV/zip（Issue #18）；举报人可见导出；附件证据上传。
