## 1. Model.Api：证据 snapshot 与 retention（TDD）

- [x] 1.1 新建 `ForumReportEvidenceSnapshotRecord` 模型；`ForumMongoSetup` 注册集合 `forum_report_evidence_snapshots` 与 `(reportId, handledAtUtc)` 唯一索引
- [x] 1.2 新建 `ForumReportEvidenceSnapshotWriter`：结案时写 report/target 快照；`reportId`+`handledAtUtc` 幂等；失败 log 不回滚 PATCH
- [x] 1.3 测试：PATCH → resolved/rejected 写 snapshot；acknowledge 不写；重复结案幂等；reopen 再结案写新 snapshot
- [x] 1.4 扩展 `ForumReportRetentionPurger`（或同 Job）purge 过期 snapshot（与 `ClosedRetentionDays` 同 cutoff）
- [x] 1.5 测试：`ForumReportEvidenceRetentionTests` 覆盖 snapshot 与 report 同期删除、audit 保留
- [x] 1.6 `ModReportsController.PatchStatus` 接入 `ForumReportEvidenceSnapshotWriter`
- [x] 1.7 运行 `dotnet test --filter "FullyQualifiedName~ForumReportEvidence"` 通过

## 2. Model.Api：EvidenceZipBuilder 与导出端点（TDD）

- [x] 2.1 实现 `EvidenceZipBuilder` + `EvidenceBundleInput`：输出含 manifest/report/target/thread-audit/sanctions-summary/readme 的 zip
- [x] 2.2 实现 `GET /api/mod/reports/{reportId}/evidence`：closed 200 zip；pending 400 `REPORT_NOT_CLOSED`；越权 403；purge 后 404 `EVIDENCE_EXPIRED`
- [x] 2.3 测试：admin 导出 closed；pending 拒绝；report purge 后 snapshot 仍可导出；sanctions-summary 来自 audit 无 User 调用；版区越权 403
- [x] 2.4 运行 `dotnet test --filter "FullyQualifiedName~ForumReportEvidence"` 通过

## 3. 前端：举报队列导出证据包

- [x] 3.1 `clients.ts`：`downloadModReportEvidence(reportId)` blob 下载 helper
- [x] 3.2 `useModerationReportsQueue` + `ModerationReportsQueueView`：closed 行「导出证据包」`el-button type="primary"`；pending 不展示或 disabled + 提示
- [x] 3.3 错误态：`REPORT_NOT_CLOSED` / `EVIDENCE_EXPIRED` 用户可见反馈；样式 `forum-tokens.css`
- [x] 3.4 可选：`clients` 单元测试或 vitest mock blob 下载路径

## 4. 验证与收尾

- [x] 4.1 `dotnet test tests/JIssWeb.Model.Api.Tests` 全量通过
- [x] 4.2 `cd frontend && npm run build` 无编译错误
- [x] 4.3 change-review 对照 delta specs 全部 SHALL 场景
- [x] 4.4 手工：结案 → 导出 zip → 解压核对 JSON；pending 无导出；接近 retention 提示（若已实现 DTO 字段）
