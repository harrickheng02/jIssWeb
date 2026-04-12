## Why

M2 需要「论坛帖子搜索」可联调闭环：`pm-plan` 要求按标题或作者关键词检索、分页一致、前端防抖与后端限流；当前仅有首页壳上的搜索框占位，尚无契约化检索行为。

## What Changes

- 新增公开论坛帖子搜索 HTTP 能力（查询参数、分页、响应与 Feed 摘要字段对齐），匹配策略为标题 **或** 作者标识（`sub` 精确/前缀或实现选用的一致规则），不包含正文全文检索与复杂排序。
- 在网关后的论坛 API 上对搜索路由施加限流（配额可配置），超限返回可区分状态（如 429）与统一错误体。
- 首页顶栏搜索输入：输入变更防抖、空查询不反复打接口、可与列表区或结果区联调；回车可立即提交一次搜索。
- MongoDB 侧为搜索字段建立合适索引或查询路径（与现有帖子集合一致），不引入独立搜索引擎。

## Capabilities

### New Capabilities

- `forum-post-search`: 论坛帖子关键词搜索的 API 契约、限流与错误语义；与 `forum-content-api` 列表摘要字段对齐；明确非目标（无 Elasticsearch、无多语言分词、无高级排序）。

### Modified Capabilities

- `forum-homepage-shell`: 顶栏搜索输入 SHALL 触发基于上述 API 的检索行为（含防抖与空/失败/空结果可感知），而非仅静态占位。
- `model-service`: SHALL 实现搜索查询与索引支撑，与 `forum-post-search` 及既有论坛持久化一致。

## Impact

- 后端：`JIssWeb.Model.Api` 论坛控制器与领域/仓储；Mongo 索引与配置；可选 Redis 计数限流。
- 网关：`/api/forum` 转发不变；若限流在网关则需配置（优先应用内限流以降低网关分叉）。
- 前端：`HomeView` 或共享搜索组件、axios 调用、防抖与 429 提示。
- 依赖：`token-identity-consistency`（作者键与 `sub` 一致）；`forum-content-api` 分页与错误约定。
