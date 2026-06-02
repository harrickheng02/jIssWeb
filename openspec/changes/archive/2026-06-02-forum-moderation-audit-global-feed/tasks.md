## 1. 配置与动作码

- [x] 1.1 新增 `Forum:ModerationAudit` 选项（`DefaultFeedDays`、`MaxExportRows`）并写入 `appsettings.Local.example.json` 注释
- [x] 1.2 在 `ModerationAuditActions.Known` 与 `ModerationAuditPresentation` 注册 `audit.export` 中文 label

## 2. 后端 — Feed 查询

- [x] 2.1 实现 `AuditFeedQuery`（时间/action/board/角色范围 filter，默认 30 天）
- [x] 2.2 `ForumMongoSetup` 增加 feed 用 `occurredAtUtc` + `metadata.boardId` 索引
- [x] 2.3 `ModAuditController` 增加 `GET feed`：分页 DTO（含 boardLabel/postId/reportId）
- [x] 2.4 集成测试 `ForumModerationAuditFeedTests`：admin 全站、版主默认范围、越权 board、action/时间筛选、缺 boardId 行排除、export 排序

## 3. 后端 — CSV 导出

- [x] 3.1 `ModAuditController` 增加 `GET export`：CSV 生成、`EXPORT_TOO_LARGE`
- [x] 3.2 导出成功后写 `audit.export` 审计行（失败仅 log）
- [x] 3.3 集成测试：成功导出列头与行数、超限 400、版主范围、export 写审计

## 4. 前端 — API 客户端

- [x] 4.1 `clients.ts` 增加 `listModerationAuditFeed` / `exportModerationAuditCsv`（query 序列化与按帖 audit 一致）
- [x] 4.2 Vitest `clients.moderation-audit-feed.test.ts`、`moderationAuditActions.test.ts` 覆盖参数序列化与筛选常量

## 5. 前端 — IA 与页面

- [x] 5.1 新增 `ModerationHubTabs.vue`；`/moderation/audit` 路由（`requiresModerate`）
- [x] 5.2 实现 `ModerationAuditFeedView.vue`：筛选、表格、分页、空/错态（`forum-tokens.css`）
- [x] 5.3 治理工作台 layout：`/moderation` 默认审计动态；Tab 含审计/举报/标签；`/admin/tags` 重定向；顶栏「治理」直达
- [x] 5.4 导出按钮与 `EXPORT_TOO_LARGE` 提示

## 6. 收尾

- [x] 6.1 全量 `dotnet test` 相关 filter + `npx vitest run` 受影响 spec
- [x] 6.2 更新 `pm-plan.yaml` Issue #18「后续方向」标注本 change 名（可选，与 archive 同步）
