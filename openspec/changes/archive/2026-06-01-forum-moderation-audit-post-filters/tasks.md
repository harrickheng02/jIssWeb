## 1. Model.Api：审计查询扩展（TDD）

- [x] 1.1 新建 `ForumModerationAuditFilterTests`：按 `action`、`fromUtc`/`toUtc` 筛选、非法时间范围 400、未知 action 400、分页边界
- [x] 1.2 扩展 `BuildPostThreadAuditFilter`：纳入 `user.*`（`Metadata.postId`）与 `report.*`（`Metadata.postId`）分支
- [x] 1.3 `ModAuditController.List` 增加 `action`/`fromUtc`/`toUtc` 参数解析与 filter 组合
- [x] 1.4 `ModerationAuditPresentation` 增加 `report.acknowledge` / `report.reject` 及既有 report 动作中文 label
- [x] 1.5 运行 `dotnet test --filter "FullyQualifiedName~AuditFilter"` 通过

## 2. Model.Api：举报 workflow 写审计（TDD）

- [x] 2.1 测试：`PatchStatus` → resolved/rejected 写 `report.resolve`/`report.reject` audit（含 `postId`/`boardId`）；reopen 不写；重复 HTTP 幂等
- [x] 2.2 测试：`Acknowledge` 写 `report.acknowledge`；重复 acknowledge 审计幂等
- [x] 2.3 `ModReportsController` 注入 audit 集合并实现写审计 helper（失败 log，不回滚主事务）
- [x] 2.4 运行 `dotnet test --filter "FullyQualifiedName~ReportNotification|FullyQualifiedName~Audit"` 通过

## 3. Model.Api：处罚 metadata 补齐（TDD）

- [x] 3.1 测试：report 队列下发 warning/mute/unmute 审计含 `metadata.postId`；帖线程 audit 可查到 `user.warn`
- [x] 3.2 `ModUserSanctionsController` 写审计时从 report 填充 `postId`/`boardId`
- [x] 3.3 运行 `dotnet test --filter "FullyQualifiedName~Sanction|FullyQualifiedName~Audit"` 通过

## 4. 前端：操作记录抽屉筛选与分页

- [x] 4.1 `clients.ts`：`listModerationAuditByPost` 增加 `action`/`fromUtc`/`toUtc`/`page`/`pageSize` 可选参数
- [x] 4.2 `ForumPostGovernancePanel`：操作类型筛选、时间范围、`el-pagination`；筛选变更重置页码；空态/错误态
- [x] 4.3 样式使用 `forum-tokens.css` 变量，无硬编码色值/间距

## 5. 验证与收尾

- [x] 5.1 `dotnet test tests/JIssWeb.Model.Api.Tests` 全量通过
- [x] 5.2 `cd frontend && npm run build` 无编译错误
- [x] 5.3 change-review 对照 delta specs 全部 SHALL 场景
- [x] 5.4 手工：帖详情操作记录筛选/翻页；举报受理/结案后帖审计可见对应行
