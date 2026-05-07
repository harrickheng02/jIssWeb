## 0. 与根规范的关系

本变更目录下的 `specs/**` 为提案期 ADDED 快照；**最新契约以 `openspec/specs/forum-report-api`、`openspec/specs/forum-report-moderation-ui` 为准**（含后续「三态 + 仅 `status` 的 PATCH（仅 `forum_reports`）+ 队列展开治理 + 治理页进队列」等演进）。

## 1. OpenSpec 与契约

- [x] 1.1 评审本变更下 `specs/**` 与 `design.md`；**HTTP 枚举与审计名以根 `openspec/specs/forum-report-api`、`openspec/specs/forum-report-moderation-ui` 为准**（`design` 与 change 内 specs 已与之对齐）
- [x] 1.2 实现阶段完成后将 delta 合入 `openspec/specs/**` 并在归档变更中记录（按仓库 OpenSpec 归档流程）

## 2. Model.Api：数据与索引

- [x] 2.1 新增举报文档模型（含 `reporterSub`、`targetType`、`targetId`、`boardId`、`status`、`reason`、时间戳、`handledBySub`/`handledAtUtc` 等 design 约定字段）
- [x] 2.2 在 `ForumMongoSetup` 注册集合名常量、创建索引（至少：`status`+`createdAt` 列表；`targetType`+`targetId`+`reporterSub`+`pending` 防重复；（`boardId`+`createdAt`) 可选）
- [x] 2.3 实现从 post/reply id 校验目标存在且未删除、并解析 `boardId` 的辅助逻辑（复用现有论坛文档访问方式）

## 3. Model.Api：`POST /api/forum/reports`

- [x] 3.1 新增 Controller/端点：鉴权 [Authorize]、请求体验证、`pending` 初始状态、重复 pending 返回 409 与统一错误码
- [x] 3.2 覆盖 401/404/400/409 与成功体的集成测试

## 4. Model.Api：`GET /api/mod/reports` 与 `PATCH /api/mod/reports/{reportId}`

- [x] 4.1 列表：分页、可选 `status` 过滤；接入 `ForumModerationAccessService`（或等价）实现 admin 全量、moderator 按 `forumBoardIds` 裁剪
- [x] 4.2 更新状态：`PATCH` body 为 **`{ "status": ... }`**（`pending` \| `rejected` \| `resolved`，`dismissed`/`acknowledged` 映射同上）；**任意状态可再 PATCH**；`pending` 清空处理人与 `ResolutionCode`；`rejected`/`resolved` 写处理人与时间；403/404/401 与根 **`forum-report-api`** 一致
- [x] 4.3 成功 `PATCH` 仅更新 **`forum_reports`**（不写举报状态类 **`forum_moderation_audit`**；与根 **`forum-report-api`**、`ModReportsController` 一致）
- [x] 4.4 集成测试：member 403、moderator 版区内/外、admin 全站、非法状态流转

## 4A. Model.Api：已结案 **`forum_reports`** 保留

- [x] **`Forum:ReportRetention`**（`Enabled`、`ClosedRetentionDays`、`IntervalHours`、`StartupDelayMinutes`）+ **`ForumReportRetentionPurger`** + **`ForumReportRetentionPurgeHostedService`**；**`pending`** 不匹配删除条件；结案且 **`HandledAtUtc`** 早于保留窗口的文档硬删；审计表不随该 Job 删除
- [x] `ForumMongoSetup` 增加 **`status` + `HandledAtUtc`** 复合索引以利清理扫描
- [x] **`ForumReportRetentionPurgerTests`** 覆盖删旧留新与 **`pending`**

## 5. 网关与配置

- [x] 5.1 核对 YARP（或当前网关）已转发 `/api/forum/**`、`/api/mod/**`；若新增路径不在通配内则补路由并文档化

## 6. 前端：API 与类型

- [x] 6.1 在 `frontend` API 客户端增加举报提交、mod 列表、mod 更新类型的请求函数与 DTO 对齐后端

## 7. 前端：用户举报 UI

- [x] 7.1 帖子详情主帖与回复列表增加举报入口；已登录可用，未登录引导登录
- [x] 7.2 举报表单（`reason` 可选）与成功/失败/409 提示

## 8. 前端：处理队列 UI

- [x] 8.1 在 **`/moderation` 治理说明页** 提供「举报队列」入口达 **`/moderation/reports`**（角色规则与 `forum-moderation-sticky-ui` 一致；顶栏仅进治理总览）
- [x] 8.2 队列页：分页列表、**默认 `GET` `status=pending`**、可按状态筛选、「全部」不传 `status`、三态 **`PATCH`（仅 `status`）**、401/403 提示
- [x] 8.3 从列表跳转到帖子详情（`targetType`/`targetId` 深链）便于人工判定

## 9. 验收

- [x] 9.1 新增或更新 `manual-qa.md`（或等价）描述普通用户举报 + 版主/管理员处理演示步骤，并引用 `pm-plan` Issue #4
