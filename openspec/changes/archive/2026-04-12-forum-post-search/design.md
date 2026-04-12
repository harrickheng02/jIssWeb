## Context

论坛已有 `GET /api/forum/posts`（`page`/`pageSize`、`boardId`）与 `ForumPostRecord`（含 `Title`、`AuthorSubId`）。M2 需在不变更网关路径前缀的前提下增加关键词检索，并在前后端落实防抖与限流。

## Goals / Non-Goals

**Goals:**

- 公开 GET 检索：关键词同时匹配标题（不区分大小写子串）或作者 `sub`（`AuthorSubId` 子串匹配，与展示名解耦）。
- 分页与校验与现有列表一致（`page`≥1，`pageSize` 1–50）。
- 前端顶栏搜索：防抖、回车立即请求、空串不发请求；可展示加载/空/错/429。
- 对「带搜索条件」的请求实施限流，避免扫库与滥用。

**Non-Goals:**

- 正文全文检索、Elasticsearch、多语言分词、相关度排序、搜索历史与联想。

## Decisions

1. **接口形态**：在 `GET /api/forum/posts` 上增加可选查询参数 `q`（或项目内统一命名 `search`；实现择一并在 API 契约中固定）。若仅 `boardId`/`page` 而无 `q`，行为与现有一致；若带 `q` 则进入搜索分支。  
   - 备选独立路径 `GET /api/forum/posts/search`：更少误用，但多一条路由与网关文档；**选用同一资源 + `q`** 以降低表面复杂度。

2. **空 `q`**：`q` 出现且 trim 后为空 → **400** + 统一错误码（如 `INVALID_SEARCH_QUERY`）；不出现 `q` → 非搜索列表。

3. **Mongo 查询**：`$or`：`Title` 用正则（转义特殊字符）不区分大小写；`AuthorSubId` 用包含匹配。与 `boardId` 同时存在时 **AND**。排序与默认列表一致（如创建时间倒序）。  
   - 索引：在 `Title`（文本或正则友好索引策略）、`AuthorSubId` 上建复合/单字段索引；若正则前缀不可索引则接受 MVP 下全表扫描风险并由限流约束——**优先**对 `AuthorSubId` 精确前缀用范围查询、标题用 `$regex` 前缀时可部分用索引。

4. **限流**：在 Model API 进程内对「含 `q` 的 GET `/api/forum/posts`」按 **客户端 IP**（`X-Forwarded-For` 首段或连接 IP）滑动/固定窗口，配额可配置；超限 **429**，body 仍用统一错误结构。  
   - 备选 Redis + 计数：与 docker-compose 已有 Redis 一致；无 Redis 时退化内存窗口（单机）。**首选 Redis** 若项目已注入。

5. **前端防抖**：`watch`/`@input` 防抖约 **300ms**；`keydown.enter` 立即触发一次有效查询并取消防抖队列中的重复。

## Risks / Trade-offs

- **[Risk]** 正则标题搜索在数据量大时偏慢 → **Mitigation**：`pageSize` 上限、限流、后续可加标题前缀索引或专用检索服务（非本变更）。
- **[Risk]** 仅 `sub` 子串匹配对用户不直观（用户输入昵称） → **Mitigation**：范围上符合 pm-plan「作者关键词」与身份一致性；昵称搜索列入非目标。
- **[Risk]** 限流误伤共享 NAT → **Mitigation**：配额按环境调优；后续可加登录用户更高配额。

## Migration Plan

- 部署顺序：先上索引与后端（兼容旧客户端），再上前端接线。  
- 回滚：去掉 `q` 处理与限流中间件；索引可保留。

## Open Questions

- 错误码字符串是否与现有 `INVALID_BOARD_ID` 同一枚举表（由实现对照现有 `ApiResult`）。
