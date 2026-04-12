## Context

版区已由 `ForumBoardsOptions` + `GET /api/forum/boards` 与列表 `boardId` 对齐。帖子存 `Tags`（字符串列表），列表 DTO 已含 `tags`。`GET /api/forum/posts` 的 `q` 由 `forum-post-search` 约束为标题与 `AuthorSubId` 子串，不含标签。首页 `HomeView` 右侧 `hotTags` 为前端常量。

## Goals / Non-Goals

**Goals:**

- 热门标签与帖子标签词表同源（服务端对帖子 `Tags` 聚合）。
- 用户点击某一热门标签时，主 Feed 仅展示带该标签的帖子（与当前 `boardId` 筛选 AND）。
- 失败、空列表、加载中与现有列表请求行为一致，可被用户区分。

**Non-Goals:**

- 标签/版区的创建、编辑、删除、审核流。
- 推荐算法、热度排序策略、运营配置化榜单（归 M2+）。
- 修改 `q` 的匹配字段或搜索限流策略。

## Decisions

1. **独立查询参数 `tag` 而非扩展 `q`**：精确表达「按标签筛帖」，避免与「关键词搜索」及 429 限流语义绑定；实现为 Mongo `Tags` 数组包含匹配（大小写不敏感 trim 后相等或项目约定的一致规则）。
2. **`GET /api/forum/tags/popular`**：匿名只读；查询参数 `boardId` 可选（与列表一致，未知 `boardId` 返回 400 与列表同源）；`limit` 默认与上限在实现中固定（如默认 20、最大 50）。聚合在服务端一次性完成，避免前端拉全量帖统计。
3. **组合过滤**：`boardId` + `tag` + `q` 同时存在时为 AND；`tag` 与 `q` 同时存在时搜索仍走现有 rate limit 规则（仅对含非空 `q` 的请求），仅 `tag` 不带 `q` 则不限于搜索限流类配置（与现列表一致）。
4. **前端状态**：用 `route.query.tag` 或等价单一来源驱动 `listForumPosts`，与 `boardId`（侧栏）同步；清空标签时移除 query 或置空并 refetch。

## Risks / Trade-offs

- [聚合性能] → 标签 popular 用 Mongo aggregation + limit；数据量极大时再索引或缓存（本阶段不强制）。
- [空库] → 接口返回空数组，UI 展示与「无标签」空态一致。
- [Yarp] → 若已通配 `/api/forum/**` 则无需改；否则补路由。

## Migration Plan

向后兼容：新 query 与 path 均为可选/新增；无数据迁移。回滚：移除前端调用与新参数即可。

## Open Questions

无；若产品坚持「点标签必须走 `q`」则与当前搜索 spec 冲突，需另开变更改 `forum-post-search`。
