## Context

JWT `sub` 与发帖/回复写入已在 `token-identity-consistency`、`forum-content-api` 对齐；Mongo 中帖子、回复作者字段为 `AuthorSubId`（对外 DTO 为 `AuthorId`，值等于 `sub`）。个人中心与 M2 通知仍缺「按当前用户读」的统一约定，需在规范与实现上补齐列表端点并禁止信任客户端用户键。

## Goals / Non-Goals

**Goals:**

- 论坛服务提供仅登录可调用的「我的帖子」「我的回复」列表，过滤键仅来自令牌 `sub`。
- 网关/BFF 若转发此类请求，不引入第二套用户 id；下游仅依赖已校验身份。
- Mongo 为 `AuthorSubId` 增加支撑按用户分页查询的索引（与现有集合一致）。

**Non-Goals:**

- 站内通知持久化与投递（由 M2 通知 Issue 实现，仅约定收件人键与 `sub` 一致）。
- 跨租户、账号合并、管理员代查他人稿件。

## Decisions

1. **路由形态**：在 Model/论坛 API 使用 `GET /api/forum/me/posts` 与 `GET /api/forum/me/replies`（或同组 `me` 前缀），与公开 `GET /api/forum/posts` 区分，避免与匿名列表共用路径产生歧义。备选 `?scope=mine` 与公开列表同路径：未采用，易误配缓存与鉴权。
2. **鉴权**：两接口均 `[Authorize]`，作者过滤仅用 `User`/claims 中的 `sub`，**不接受** body/query 中的用户 id。
3. **分页**：与公开列表一致的有效 `page`/`pageSize` 上限约定，返回同一 `PagedPostsDto` 形态（回复列表可为等价分页 DTO）。
4. **索引**：`ForumPostRecord.AuthorSubId` + `CreatedAtUtc` 降序；`ForumReplyRecord.AuthorSubId` + `CreatedAtUtc` 降序（或按产品排序）。

## Risks / Trade-offs

- **[Risk]** `me` 路由与 `{postId}` 冲突 → **缓解**：路由注册顺序让 `me` 字面量优先于通配 id（ASP.NET Core 按注册顺序/更具体模板优先，需在实现中验证）。
- **[Risk]** 前端误调公开列表并传作者过滤 → **缓解**：规范明确作者筛选仅出现在 `me` 端点，公开列表不增加可伪造的作者 query。

## Migration Plan

新增端点与索引，无数据迁移。部署后建索引；若集合已有数据，索引可在后台构建。

## Open Questions

- 个人中心是否经 BFF 聚合 Profile + 论坛：若经 BFF，仍调用论坛「当前用户」语义接口，不在 BFF 用 query 拼用户 id。
