## Context

- 产品验收见 `scripts/github-sync/pm-plan.yaml` Issue #4；前置 Issue #17（JWT `forumRole`、`forumBoardIds`）与 Issue #7（`/api/mod/posts/**`、`/api/mod/audit`、Mongo `forum_moderation_audit`）已交付。
- 举报与「版主直接改帖」是不同业务对象：权威状态放在**独立集合**（`forum_reports`），避免与帖子文档强耦合。
- 前端与网关已习惯 `/api/forum/**` 面向用户操作、`/api/mod/**` 面向治理操作；本设计沿用该分界。
- **契约细节**：以根目录 **`openspec/specs/forum-report-api`**、**`openspec/specs/forum-report-moderation-ui`** 为权威；本节描述架构决策骨架。

## Goals / Non-Goals

**Goals:**

- 已登录用户可对**帖子**或**回复**创建举报；记录含举报人 `sub`、目标类型与 id、版区信息（便于版主过滤）、时间戳与可流转状态。
- 版主仅见**其版区范围内**目标对应的举报；管理员见全量；列表分页；列表可按 **`pending` / `rejected` / `resolved`** 筛选；单行状态可在三态间调整以演示闭环。
- 每次成功的举报状态 **`PATCH`** 写入 `forum_moderation_audit`（与置顶审计并列），便于后续 Issue #18 统一「治理时间线」。

**Non-Goals:**

- 工单 SLA、法务级证据保全、多租户隔离（见 pm-plan `deferred_scope`）。
- 自动审核、反垃圾策略（Issue #5 另项）。
- 举报人身份对版主脱敏、附件证据上传（可列入后续 change）。

## Decisions

1. **集合与主键**
   - Mongo 集合名：`forum_reports`（与 `ForumMongoSetup` 常量注册、复合索引一并管理）。
   - 文档 id：服务端生成 `ObjectId` 或 UUID 字符串，对外 API 统一 string。

2. **HTTP 面**
   - `POST /api/forum/reports`：请求体含 `targetType`（`post` \| `reply`）、`targetId`、可选 `reason`（短文本，上限在实现中固定）。服务端校验目标存在且未删除；从目标推导 `boardId` 写入举报文档；初始 **`status=pending`**。
   - `GET /api/mod/reports`：`page`、`pageSize`、可选 `status`（**`pending` \| `rejected` \| `resolved`**；服务端将存量 **`dismissed`** 归入 **`rejected`**、**`acknowledged`** 归入 **`resolved`**）；版主结果集按 moderator 版区范围与 **`boardId`** 裁剪；管理员不加版区过滤。列表响应 DTO **不含** **`resolutionCode`**（工作流仅存三态）。
   - `PATCH /api/mod/reports/{reportId}`：请求体 **`{ "status": "<value>" }`**。接受 **`pending`**、**`rejected`**、**`resolved`**，以及别名 **`dismissed`**（存 **`rejected`**）、**`acknowledged`**（存 **`resolved`**）。**任意当前状态可再次 PATCH**（含从终态回到 **`pending`**）。
   - 设 **`pending`**：清空 **`handledBySub`**、**`handledAtUtc`**，并清空 **`ResolutionCode`**。
   - 设 **`rejected`** / **`resolved`**：写入 **`handledBySub`**（操作者 `sub`）、**`handledAtUtc`**，并清空 **`ResolutionCode`**。
   - **硬删除帖子或回复**：由 **`forum-moderation-delete-content`** 下 **`DELETE /api/mod/posts/*`**、**`DELETE /api/mod/replies/*`** 独立完成，**不经**本条 **`PATCH`** 编排。

3. **重复举报**
   - 同一 `reporterSub` + `targetType` + `targetId` 在已存在 **`pending`** 记录时，`POST` 返回 **409** 与统一错误码（例如 `DUPLICATE_PENDING_REPORT`）。对已结案的历史记录允许再次举报产生新 **`pending`**（产品若改规则可另开变更）。

4. **审计对齐**
   - **`PATCH`** 举报状态：与根 **`forum-report-api`** 一致，更新 **`forum_reports`** 工作流字段（**`ModReportsController`**）。
   - **硬删除**：由 **`DELETE`** 路径写入 **`post.modDelete`** / **`reply.modDelete`**（**`forum-moderation-delete-content`**）。

5. **前端 IA**
   - 举报人：帖子详情主帖与回复楼层入口，调用 `POST /api/forum/reports`。
   - 处理人：从 **`/moderation` 治理说明页** 进入 **`/moderation/reports`** 举报队列；行内三态（待处理 / 已驳回 / 已处置）对应 **`PATCH`** 仅传 **`status`**；可见性与 **`forum-moderation-sticky-ui`** 一致（moderator/admin）。
   - 列表由后端按版区裁剪；前端**不**仅靠隐藏 UI 做多租户隔离。

6. **错误与鉴权**
   - 与现有 Model.Api 一致：401 未认证、403 角色或版区不足、404 目标或举报 id 不存在、400 参数非法（含无效 **`status`**）、409 重复 pending。

## Risks / Trade-offs

- **[Risk]** 回复删帖后举报悬挂 → **缓解**：列表仍展示，`targetId` 跳转若 404 则展示占位（见帖子详情容错）。
- **[Risk]** 删除类治理与举报工单状态分路径 → **缓解**：前端 **`DELETE`** 与 **`PATCH`** 解耦；集成测试覆盖版区与 403。
- **[Trade-off]** 三态不编码「事由码」；删内容在帖子详情治理区 **`DELETE`** 完成，与举报单状态分列。
- **[Trade-off]** 已结案 **`forum_reports`** 行按 **`Forum:ReportRetention`** 过期硬删，控制集合体量； **`forum_moderation_audit`** 仍可追溯操作；更长合规留存可对审计单列策略。

1. 部署含新集合与索引的 Model.Api；旧环境无数据迁移。
2. 前端与网关：新路由落在现有 `/api/forum`、`/api/mod` 通配下时可不改 YARP；否则补转发。
3. 回滚：下线新 Controller 与前端路由；`forum_reports` 数据可保留。

## Open Questions

- `reason` 采用自由文本还是固定下拉（涉黄/广告/骚扰/其他）：首版**短文本**；分类可后续加字段。
- 是否在 `GET /api/mod/reports` 响应中内嵌目标标题摘要：首版 id + board + 时间 + 状态 + 深链；强需求时再加轻量冗余字段。
