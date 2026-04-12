## Why

首页右侧「热门标签」仍为静态词表，与帖子 `tags` 及 `GET /api/forum/boards` 不同源，违背 M1 收尾条目与 `scripts/gitee-sync/pm-plan.yaml` 中「标签同源、验收口径写死」的约定；列表亦缺少按标签筛选的明确契约，无法演示「点标签 → 列表」闭环。

## What Changes

- Model.Api：为帖子列表增加可选 `tag` 查询参数（与现有 `boardId`、`q` 组合为 AND）；新增只读 `GET /api/forum/tags/popular`（或等价路径）从已存帖子聚合热门标签，支持可选 `boardId` 与上限。
- 前端首页：右侧热门标签改为调用上述接口；点击标签时更新路由或状态并触发列表请求（与现有加载/空/错态一致）；移除与数据源无关的硬编码标签数组。
- 规范：`forum-homepage-shell` 与 `forum-content-api` 增量描述上述行为；不改变发帖鉴权模型。

## Capabilities

### New Capabilities

（无；行为归入既有能力增量。）

### Modified Capabilities

- `forum-homepage-shell`：右侧热门标签数据来源、点击与主列表联动、空态与列表态一致性的要求补充。
- `forum-content-api`：帖子列表可选 `tag` 筛选；只读热门标签 HTTP 契约。

## Impact

- 代码：`JIssWeb.Model.Api`（`ForumPostsController`、新建或扩展 `ForumConfigController`）、`frontend`（`HomeView.vue`、`clients.ts`）、可选 Yarp 若路径未覆盖。
- 测试：`ForumPostsSearchTests` 或同类集成测试扩展；新 endpoint 最小用例。
- 与 `forum-post-search`：`q` 仍仅标题与作者子串（现状）；本变更用独立 `tag` 参数避免与搜索限流语义混淆。

## 非目标与后续若实现时的模块依赖

本变更**不**实现以下能力；若产品后续要做，主要依赖 `scripts/gitee-sync/pm-plan.yaml` 中的模块划分如下（便于排期与拆 Issue）：

| 非目标（本变更） | 后续主要涉及模块 |
|------------------|------------------|
| 版主编辑、分区/标签后台 CRUD | **治理与审核**（角色与操作审计）；**版区与标签**（配置源若从静态 options 迁到 DB/CMS）；**账号与访问**（管理端鉴权、与 JWT claims 扩展的协调） |
| 个性化推荐、算法化热榜 | **搜索与发现**；**运营与公告**；**平台与基础设施**（报表/BFF）；与 M2「公告位与热门数据接口」及 `report-service` 等规范衔接 |

以上模块**不**阻塞本变更交付；仅说明超出范围工作包落在何处。
