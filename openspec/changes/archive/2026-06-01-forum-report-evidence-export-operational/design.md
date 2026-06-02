## Context

Issue #19 change-C。`forum_reports` 结案后按 `Forum:ReportRetention:ClosedRetentionDays`（默认 120）硬删；`forum_moderation_audit` 不因该 Job 删除。前端举报队列已提示「结案超保留期从库移除时审计可查」，但审计无帖子正文、无工单元数据离线包。change-A/B 与 Issue #22 已交付处罚、受理、结案审计。

## Goals / Non-Goals

**Goals:**

- 结案（`resolved`/`rejected`）时写入 evidence snapshot（幂等；失败不阻断 PATCH）。
- `GET /api/mod/reports/{reportId}/evidence` 仅 closed 返回 zip；pending → `400 REPORT_NOT_CLOSED`。
- `EvidenceZipBuilder` 独立组件，供 Issue #18 审计导出复用。
- snapshot 与 `forum_reports` 共用 `ClosedRetentionDays` 一并 purge。
- 前端举报队列 closed 行「导出证据包」；`forum-tokens.css` + `el-button type="primary"`。

**Non-Goals:**

- 法务级哈希链 / WORM；pending / acknowledge 阶段导出或写 snapshot。
- User 内网 sanctions 全量；批量审计 CSV/zip（Issue #18）。
- 附件证据上传；举报人可见导出。

## Decisions

### 1. 新集合 `forum_report_evidence_snapshots`

**选择**：Mongo 文档含 `reportId`、`handledAtUtc`（自 report 复制，purge 键）、`snapshottedAtUtc`、`report`（JSON 快照）、`target`（post/reply 快照或 tombstone）。

**理由**：工单 purge 后仍可导出；正文可能在 soft-delete / modDelete 后消失，结案瞬间抓取 target 价值最大。

**幂等键**：`reportId` + `handledAtUtc` 唯一；重复 PATCH 重试或同 `HandledAtUtc` 结案不重复写。

**不写 snapshot 的时机**：`acknowledge`、仍为 `pending`、reopen 本身。

### 2. 快照内容 vs 导出时拼装

| 部分 | 来源 |
|------|------|
| `report.json` | snapshot.report（report 已 purge 时唯一来源） |
| `target.json` | snapshot.target |
| `thread-audit.json` | 导出时查 `forum_moderation_audit`（`Metadata.reportId` 或帖线程 filter） |
| `sanctions-summary.json` | 导出时从 audit 投影 `user.warn`/`user.mute`/`user.unmute`（无 User 内网） |
| `manifest.json` | 导出时生成（`exportVersion`、`exportedAtUtc`、`exportedBySub`） |

**理由**：审计长期保留且可能追加（reopen 再结案）；处罚摘要已在 audit metadata；snapshot 只冻结「结案时刻的工单与内容」。

### 3. `EvidenceZipBuilder`

**选择**：`Services/EvidenceZipBuilder.cs`，输入 `EvidenceBundleInput`（report 快照、target 快照、audit 行枚举、manifest 元数据），输出 `MemoryStream` 或 `byte[]` zip。

**理由**：Issue #18 后续传入多 audit 行即可复用打包逻辑；首期工作量小。

zip 条目（UTF-8 JSON + 可选 `readme.txt`）：

```
manifest.json
report.json
target.json
thread-audit.json
sanctions-summary.json
readme.txt
```

文件名：`report-{reportId}-evidence.zip`（Content-Disposition）。

### 4. 导出 API

```
GET /api/mod/reports/{reportId}/evidence
```

- 鉴权：`RequireForumModerator` + `ForumModerationAccessService` 版区范围（与 `ModReportsController` 一致）。
- report 存在且 canonical closed → 200 `application/zip`。
- report `pending` → `400 REPORT_NOT_CLOSED`。
- report 不存在：若 snapshot 存在且 caller 有 scope → 200；否则 `404`。
- report 与 snapshot 均已 purge → `404 EVIDENCE_EXPIRED`。
- 同步流式响应（纯文本体量足够）。

### 5. Retention 协调

**选择**：扩展 `ForumReportRetentionPurger`（或同 HostedService 内第二 pass）对 `forum_report_evidence_snapshots` 执行：

```
HandledAtUtc != null AND HandledAtUtc < now - ClosedRetentionDays → DeleteMany
```

与 `forum_reports` 同一 cutoff、同一 Job 周期；**不**新增独立 TTL 配置键（首期）。

**顺序**：同一 pass 内先删 reports 再删 snapshots（或并行）；audit 不动。

### 6. 结案 hook

在 `ModReportsController.PatchStatus` 成功写入 terminal closed 后调用 `ForumReportEvidenceSnapshotWriter.TryWriteAsync`（与 `ForumReportModerationAuditWriter` 并列）；异常 log warning，不回滚 PATCH。

### 7. 前端

- `ModerationReportsQueueView` / `useModerationReportsQueue`：closed 行显示「导出证据包」`el-button type="primary"`；pending（含已 acknowledge）不展示或 disabled + tooltip「结案后可导出」。
- `clients.ts`：`downloadModReportEvidence(reportId)` — axios `responseType: 'blob'`，触发浏览器下载。
- 列表 DTO 可选增加 `evidenceExpiresAtUtc`（`handledAtUtc + ClosedRetentionDays`）供提示文案；首期可由前端用 `handledAtUtc` 近似或后端补充。

## Risks / Trade-offs

- **[Risk] 结案后至导出前又有 modDelete** → target 已在 snapshot 冻结；thread-audit 导出时仍含删帖动作。
- **[Risk] reopen 再结案** → 新 `HandledAtUtc` 写新 snapshot；导出以当前 report 状态为准，匹配最新 snapshot。
- **[Risk] zip 含 reporterSub 等 PII** → 仅 mod/admin；规范声明不得外传。
- **[Trade-off] sanctions 仅 audit 摘要** → 缺 User 侧 revoke 细节；运营需另查 User 服务。
- **[Trade-off] 与 Issue #18 边界** → 本 change 仅单 reportId；Builder 可复用，feed 导出不实现。

## Migration Plan

1. 部署 Model.Api：集合 + 索引（`reportId`+`handledAtUtc` unique）、Writer、Builder、Controller action、purge 扩展。
2. 部署 Frontend：导出按钮 + blob 下载。
3. 无历史 snapshot 回填；旧已 purge 工单不可再导出。
4. 回滚：下线端点与 Writer；snapshot 数据可保留。

## Open Questions

- 无（首期 retention 复用 `ClosedRetentionDays`；差异化 TTL 后续单开）。
