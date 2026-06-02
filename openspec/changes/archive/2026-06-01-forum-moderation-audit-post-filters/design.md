## Context

Issue #18 子项 #22。`GET /api/mod/audit` 已支持按帖分页，但无 `action`/时间筛选；帖线程聚合不含 `user.*` 处罚；`ModReportsController` 的结案与已受理不写审计（`forum-report-api` Purpose 仍声明 PATCH 不写 audit）。前端 `ForumPostGovernancePanel` 固定 `page=1&pageSize=20`，无筛选与翻页。

## Goals / Non-Goals

**Goals:**

- 扩展现有按帖审计 API：可选 `action`、`fromUtc`、`toUtc` + 分页；版主鉴权不变。
- 帖线程结果集纳入关联的 `user.warn` / `user.mute` / `user.unmute` 与 `report.acknowledge` / `report.resolve` / `report.reject`。
- 举报 `Acknowledge` 与 `PATCH` 结案写审计；HTTP 幂等重试不重复行。
- 帖详情操作记录抽屉：类型筛选、时间范围、分页 UI（`forum-tokens.css`）。

**Non-Goals:**

- 全局审计 feed 页、版区跨帖筛选、CSV/zip 导出。
- 修改举报通知或处罚业务语义。

## Decisions

### 1. 扩展现有 `GET /api/mod/audit`，不新增 feed 端点

**选择**：在 `targetType=post&targetId=` 必填前提下增加可选 query 参数。

**理由**：与 #7 契约连续；前端帖详情抽屉仅改 query；避免双端点维护。

**Query 契约**：

| 参数 | 说明 |
|------|------|
| `action` | 可选；重复 query 键或逗号分隔多值；须为已知动作码 |
| `fromUtc` / `toUtc` | 可选 ISO-8601；过滤 `OccurredAtUtc`；`fromUtc > toUtc` → `400 INVALID_TIME_RANGE` |
| `page` / `pageSize` | 现有规则（pageSize ≤ 50） |

筛选在 Mongo 层应用于合并后的线程 filter（见决策 2）。

### 2. 帖线程 filter 重构为 `$or` 分支 + 内存/聚合后筛选

**选择**：`BuildPostThreadAuditFilter(postId)` 扩展为：

1. 现有帖/回复/legacy report 分支（保留）。
2. **新增** `user.*` 分支：`targetType=user` 且 `Metadata.postId == postId`。
3. **新增** `report.*` 分支：`targetType=report` 且 `Metadata.postId == postId`（含新写入的 acknowledge/resolve/reject）。

**理由**：按帖查询仍走 `(targetType, targetId)` 索引的子集；user/report 行通过 `Metadata.postId` 关联，需在写入侧补齐 metadata（决策 4）。

**备选**：先查 report ids 再 `$in` reportId — 多一次查询；仅在 postId metadata 缺失的历史数据回填时再考虑。

### 3. 举报 workflow 审计动作码

| 事件 | Action | TargetType | TargetId | Metadata |
|------|--------|------------|----------|----------|
| Acknowledge 成功 | `report.acknowledge` | `report` | reportId | `reportId`, `postId`, `boardId` |
| PATCH → resolved | `report.resolve` | `report` | reportId | 同上 + `priorStatus` 可选 |
| PATCH → rejected | `report.reject` | `report` | reportId | 同上 |

`ModerationAuditPresentation` 增加中文 label（如「标记举报已受理」「结案举报」「驳回举报」）。

Legacy `report.statusChange` 仍可读，新写入统一用上表。

### 4. 幂等策略

- **`report.acknowledge`**：插入前查询是否已存在同 `targetType=report` + `targetId=reportId` + `action=report.acknowledge`；存在则跳过（Acknowledge 仍成功）。
- **PATCH 结案**：每次成功转入 `resolved`/`rejected` 写一条；HTTP 重试幂等 — 若已存在同 `reportId` + action + `Metadata.handledAtUtc`（与 report 文档 `HandledAtUtc` 一致）则跳过。
- **Reopen → 再结案**：产生新 `HandledAtUtc`，写新审计行（预期行为）。

审计写入失败 **不** 回滚 PATCH/Acknowledge 主事务（与删帖 audit 失败策略一致），仅 log warning。

### 5. 处罚审计 metadata 补齐 `postId`

**选择**：`ModUserSanctionsController` 写 `user.warn`/`user.mute`/`user.unmute` 时，当请求含 `reportId`，从 report 解析并写入 `metadata.postId`（及 `boardId`）。

**理由**：帖线程 filter 仅依赖 `Metadata.postId`，无需 join reports 集合。

### 6. 前端抽屉 UX

- 操作类型：`el-select` 多选或单选「全部」，选项与后端动作码映射到中文 label。
- 时间：`el-date-picker` datetimerange → `fromUtc`/`toUtc` ISO 字符串。
- 分页：`el-pagination`，筛选变更重置 `page=1`。
- 空列表与错误态沿用现有 panel 样式。

## Risks / Trade-offs

- **[Risk] 线程 filter `$or` 分支无法共用 `(targetType,targetId)` 索引** → 帖级动作仍走索引；user/report 分支数据量小，可接受；集成测试覆盖性能基线。
- **[Risk] 历史处罚行缺 `postId`** → 不回填脚本；仅新写入可见于帖线程（文档说明）。
- **[Trade-off] 不做全局 feed** → 运营跨帖检索留待后续 change。

## Migration Plan

1. 部署 Model.Api：扩展 filter + 写审计 + presentation labels；无破坏性 schema 变更。
2. 部署 Frontend：抽屉筛选/分页。
3. 回滚：回退代码；已写入 audit 行保留。

## Open Questions

- 无。全局 feed 与导出按 pm-plan 后续单开。
