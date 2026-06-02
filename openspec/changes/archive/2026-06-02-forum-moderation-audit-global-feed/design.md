## Context

Issue #18 后续：#22 已交付按帖 `GET /api/mod/audit?targetType=post&targetId=` 与帖详情抽屉筛选；`forum_moderation_audit` 索引为 `(targetType, targetId, occurredAtUtc↓)`，适合帖线程查询。运营需要不依赖 `postId` 的时间序横切视图与 CSV 归档。单举报证据 zip（#19 change-C）保持独立，本 change 不做批量 evidence 打包。

## Goals / Non-Goals

**Goals:**

- `GET /api/mod/audit/feed` 与 `GET /api/mod/audit/export`（CSV），筛选维度 MVP：`fromUtc`/`toUtc`、`action[]`、可选 `boardId`。
- 管理员默认全站；版主默认 `metadata.boardId ∈ forumBoardIds`；版主 UI 可清除单版区窄筛以查看全部可见版区（仍受 JWT 约束）。
- 未传时间时默认最近 30 天（UTC）；`pageSize ≤ 50`；导出行数上限 5000。
- 导出成功写 `audit.export`（`targetType=system`，metadata 含筛选摘要与导出行数）。
- 前端 `/moderation/audit` + 与举报队列共享 Tab；`forum-tokens.css`。

**Non-Goals:**

- `operatorSub`、标题关键词；evidence zip 批量打包；治理中心大 layout；改动按帖 audit 契约；异步导出队列。

## Decisions

### 1. 新端点挂在 `ModAuditController`，不放宽现有 `List` 的 `targetId` 必填

**选择**：`[HttpGet("feed")]`、`[HttpGet("export")]` 与现有 `GET /api/mod/audit` 并存。

**理由**：#22 明确「扩展现有端点、不新增 feed」仅适用于帖上下文；全局查询语义不同，避免可选 `targetId` 破坏契约。

### 2. `AuditFeedQuery` 统一 feed 与 export 的 Mongo filter

**选择**：独立静态/服务类 `BuildFeedFilter(principal, from, to, actions, boardId, scope)`，feed 与 export 共用。

**Filter 规则**：

| 角色 | 默认（无 `boardId`） | `boardId` 指定 |
|------|---------------------|----------------|
| admin | 无版区限制 | `metadata.boardId == boardId` |
| moderator | `metadata.boardId ∈ forumBoardIds` | 同上且须在 `forumBoardIds` 内，否则 403 |

时间：`OccurredAtUtc` ∈ `[fromUtc, toUtc]`（含端点，UTC）。`action` 与 #22 相同解析。

**未传 `action` 时**：feed 与 export 默认**排除** `audit.export`，避免导出操作本身刷屏动态；用户显式筛选 `audit.export` 时可查看。

**版主「全部可见版区」**：前端不传 `boardId` 即聚合全部授权版区；与「我的版区」默认数据范围相同，UI 仅在用户曾窄化到单版区后提供「重置为全部可见版区」清除 `boardId`。

**历史行缺 `metadata.boardId`**：版主 feed **排除**（防越权）；管理员可见，列表 `boardLabel` 为空或「未知」。

### 3. 索引

**选择**：新增 `{ occurredAtUtc: -1, "metadata.boardId": 1 }`（或等价 partial 索引仅含常见动作行）。

**理由**：feed 排序 `occurredAtUtc` 降序 + 版主 `boardId` `$in`；现有 `(targetType, targetId)` 索引保留给帖查询。

### 4. Feed DTO 扩展字段

在 `ModerationAuditItemDto` 或 feed 专用 DTO 增加：`boardId`、`boardLabel`（来自 metadata `boardId`/`board`）、`postId`、`reportId`（metadata 解析）、`deepLinkPath`（可选，前端亦可本地拼）。

列表批量 `ForumAuthorDisplayResolver` 解析操作人；版区标题优先 metadata，缺失时可用 `ForumBoardsOptions` 映射。

### 5. CSV 导出

- `Content-Type: text/csv; charset=utf-8`，`Content-Disposition: attachment; filename="moderation-audit-{utc}.csv"`。
- 列：发生时间(UTC ISO)、操作类型(中文 label)、操作人显示名、操作人 sub、目标类型、目标 ID、版区 ID、版区名称、关联 postId、关联 reportId。
- 超过 5000 行：`400 EXPORT_TOO_LARGE`（先 `CountDocuments`）。
- 导出请求鉴权与 feed 相同；成功后插入 `audit.export`（失败仅 log，不回滚文件流——若流已开始则优先完成下载）。

### 6. 前端 IA（最小）

- 路由：`/moderation/audit`，`meta: { requiresAuth: true, requiresModerate: true }`。
- 组件 `ModerationHubTabs.vue`（或内联）：`举报队列` → `/moderation/reports`，`审计动态` → `/moderation/audit`；两页顶部复用。
- `ModerationLayout` + 顶栏 Tab（审计动态｜举报队列｜标签管理）；`/moderation` 默认重定向审计页；`HeaderUserMenu`「治理」指向 `/moderation/audit`；`/admin/tags` 重定向。
- 筛选 UI 对齐 `ForumPostGovernancePanel` 审计区（action、datetimerange、版区 select）；管理员版区含「全站」；版主选项来自 token `forumBoardIds`（或 config API 若已有）。

### 7. 配置

`Forum:ModerationAudit:DefaultFeedDays`（默认 30）、`MaxExportRows`（默认 5000）写入 `appsettings` 节与 `Local.example` 注释。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| 缺 `boardId` 的历史审计行版主不可见 | 接受；必要时后续 backfill 脚本（非本 change） |
| 全站 feed 大表扫描 | 默认 30 天窗 + 索引；export 硬上限 |
| 导出被滥用 | `audit.export` 留痕 + 同鉴权 |
| Tab 与深链 | 保留 `/moderation/reports` 直达；Tab 仅导航增强 |

## Migration Plan

1. 部署 API + 索引创建（`ForumMongoSetup` 启动时 `CreateOne`，与现网兼容）。
2. 部署前端路由与 Tab。
3. 无数据迁移；`audit.export` 为新动作码，需在 `ModerationAuditActions.Known` 与 `ModerationAuditPresentation` 注册。

## Open Questions

（无——探索阶段四条产品决策已锁定。）
