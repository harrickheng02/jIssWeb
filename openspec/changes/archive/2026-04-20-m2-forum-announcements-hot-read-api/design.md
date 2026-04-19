## Context

首页 `forum-homepage-shell` 已要求右栏包含热门内容、热门标签与公告；`forum-content-api` 已实现帖子列表、详情、热门标签 `GET /api/forum/tags/popular`，但**公告**与**热门帖子列表**仍缺少规范级 HTTP 合同。Model.Api 当前 `GET /api/forum/posts` 仅按创建时间倒序，无 `sort`；无公告路由。本变更补齐只读接口与排序规则，支撑 pm-plan M2 Issue「公告位与热门数据接口」验收。

## Goals / Non-Goals

**Goals:**

- 提供匿名可读的公告列表 API，字段固定，空列表为合法结果。
- 提供与 Feed 卡片字段一致的「热门帖子」只读列表，排序规则可复现、可与 `boardId` / `tag` 组合（与现有过滤语义一致）。
- 在 `forum-homepage-shell` 中写明右栏公告与热门模块所依赖的端点，便于联调与测试。

**Non-Goals:**

- 个性化推荐、A/B、完整运营 CMS、算法化热榜进阶（见 pm-plan `deferred_scope` 与其它 Issue）。
- 公告的写入管理 UI（可采用配置或种子数据占位）。

## Decisions

**1. 公告：`GET /api/forum/announcements`**

- **内容**：Mongo 集合持久化公告项（`id/title/summary/linkUrl/publishedAtUtc/pinned` 等与 spec 一致），启动时可为空；后续可由脚本或管理接口演进，本期验收以只读 API 为准。
- **查询**：`limit` 整数，默认 `5`，范围 `1–50`；返回按 `pinned` 优先、`publishedAtUtc` 降序、`id` 升序打破并列。
- **缓存**：响应头 `Cache-Control: public, max-age=60`（可随实现微调，与 spec 中「可短缓存」一致）。

**2. 热门帖子：在 `GET /api/forum/posts` 上增加 `sort`**

- **取值**：`latest`（默认，与当前行为一致）、`hot`。
- **hot 排序**：在同一过滤集（`boardId`、`tag`、`q` 等）内，按 `(LikeCount desc, CommentCount desc, ViewCount desc, CreatedAtUtc desc, Id asc)` 全序排序，保证确定性。
- **与关键词搜索共存**：当存在合法非空 `q`（见 `forum-post-search`）时，**忽略 `sort`**，排序仍以搜索相关规则为准（当前实现为时间序；保持与搜索 spec 一致），避免双重语义冲突。

**3. 网关**

- 若 YARP 已按路径前缀转发 `api/forum/*`，新路径自动覆盖；若有显式路由表，追加 `announcements` 与带 `sort` 的 posts 查询透传。

## Risks / Trade-offs

- **[Risk] 热门仅用站内计数，易被刷量** → **Mitigation**：M2 占位可接受；进阶算法与风控在后续 Issue。
- **[Risk] 公告与帖子分表，运营入口未建** → **Mitigation**：开发环境种子或手工插入文档；文档写明维护方式。
- **[Risk] `sort` 与 `q` 组合语义** → **Mitigation**：设计明确「有 `q` 时忽略 `sort`」，并在集成测试中覆盖。

## Migration Plan

1. 部署后端新版本（新增集合索引、路由）。
2. 前端右栏切换至新合同（可特性开关，按任务执行）。
3. 回滚：移除路由与 UI 调用，列表回退仅 `latest`；公告右栏展示空态。

## Open Questions

- 公告 `linkUrl` 是否仅允许 `http(s)` 相对站内路径：可在实现中用校验器定稿。
