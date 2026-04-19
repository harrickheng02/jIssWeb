## Why

首页论坛壳右栏需要「公告」与「热门内容」两块可联调的只读数据；当前 `forum-content-api` 已覆盖 Feed 列表与热门标签，但缺少公告数据源与「热门帖子」的明确 HTTP 合同，无法单独验收 pm-plan Issue「公告位与热门数据接口」。

## What Changes

- 在 Model 论坛 API 层增加**公开只读**的公告列表契约（实现可为 Mongo 集合或静态配置，由设计定稿），响应字段与右栏公告模块一致，空列表可展示空态。
- 为「热门」区块约定**与 Feed 卡片一致的帖子摘要合同**：在现有 `GET /api/forum/posts` 上增加可选排序（例如 `sort=hot`），或增加专用只读热门列表端点；明确条数/分页上限、可选 `boardId` 作用域与确定性排序规则（含同分打破）。
- 在 `forum-homepage-shell` 中补充右栏「公告」「热门内容」与上述契约的对应关系（数据来源、加载/空/错状态仍遵循现有 shell 要求）。
- **BREAKING**：无（新增查询参数或新路由为向后兼容扩展；若选用新路由则旧客户端不受影响）。

## Capabilities

### New Capabilities

（无独立新能力目录；行为增量落在既有 `forum-content-api` 与 `forum-homepage-shell` 规范中。）

### Modified Capabilities

- `forum-content-api`：新增公告只读端点与热门帖子列表/排序需求；与现有 `GET /api/forum/posts`、`GET /api/forum/tags/popular` 并列说明鉴权（匿名可读）与错误码。
- `forum-homepage-shell`：右栏公告模块与热门内容模块对 `forum-content-api` 新合同的引用与字段对齐说明。

## Impact

- **后端**：`JIssWeb.Model.Api` 新增或扩展 Controller（公告、帖子列表排序）；Mongo 若存公告则需集合与索引约定。
- **网关/YARP**：若已有 forum 路由映射，需包含新路径（若有）。
- **前端**：首页右栏接入新接口，与现有帖子摘要组件复用。
- **依赖**：`openspec/specs/token-identity-consistency`（作者展示与 `sub` 一致，只读场景不变）；里程碑 M2。
