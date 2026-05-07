## Why

Issue #4（`pm-plan`）要求论坛具备「举报 — 可查询记录 — 处理侧可演示」的最小闭环；当前仓库已有版主 JWT、`/api/mod/**` 与审计能力，但缺少举报数据域与前后台路径。本变更补齐该缺口，使治理轨道与后续 Issue #18 衔接有稳定接口与 UI 锚点。

## What Changes

- 用户在已登录前提下对**帖子或回复**提交举报；服务端持久化并支持按权限查询。
- 版主在其**所辖版区**内、管理员在全站维度查看举报列表并对单条举报做**状态流转**（**`pending` / `rejected` / `resolved`**；可多次 PATCH，具体以根 **`openspec/specs/forum-report-api`** 为准）。
- 前端：内容与列表上的举报入口；版主/管理员可见的举报处理视图（可与现有 `/moderation` 导航汇合）。
- 字段与鉴权对齐现有约定：操作者与子 `sub`、目标资源标识、`forumRole` / 版区间范围沿用 `token-identity-consistency` 与既有版主校验模式。
- 可选（若 design 敲定）：处理动作写入 `forum_moderation_audit`，与置顶审计并列可查。

## Capabilities

### New Capabilities

- `forum-report-api`：Model 服务举报集合 Schema、索引与 HTTP 契约（提交、分页列表、更新状态）；错误码与 `ApiResult` 统一信封；版主版区边界与管理员全局。
- `forum-report-moderation-ui`：前台举报表单/入口；处理端列表与状态操作；与既有版主 Shell 入口一致的角色可见性规则。

### Modified Capabilities

- （留空）本闭环不修改 `token-identity-consistency` 与其它已发布 JWT 语义；不改变公开帖子列表合同除「可选深链跳转」外无强制字段变更。

## Impact

- **后端**：`JIssWeb.Model.Api` 新 Controller/服务、Mongo 集合与 `ForumMongoSetup` 索引；网关已转发 `/api/forum/**` 与 `/api/mod/**` 时仅需确认新路由落在既有前缀下（以 design 为准）。
- **前端**：`frontend` API 客户端、帖子详情与回复楼层、新版主举报处理路由或视图。
- **测试**：Model.Api 集成测试（401/403/版区边界、状态流转）。
- **依赖**：`openspec/specs/token-identity-consistency`（只读沿用）；与 `forum-moderation-post-ops`、`forum-moderation-sticky-ui` 叙事对齐。
