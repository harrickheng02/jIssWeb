## 1. 规范落地（归档前合并到 `openspec/specs/**`）

- [x] 1.1 将 `specs/forum-content-api/spec.md` 增量合并到 `openspec/specs/forum-content-api/spec.md`（ADDED 节转为正式需求）
- [x] 1.2 将 `specs/forum-homepage-shell/spec.md` 增量合并到 `openspec/specs/forum-homepage-shell/spec.md`

## 2. Model.Api：公告只读接口

- [x] 2.1 新增公告 Mongo 文档模型与集合名常量（含 `ForumMongoSetup` 注册与索引：`pinned` + `publishedAtUtc`）
- [x] 2.2 实现 `GET /api/forum/announcements`（`[AllowAnonymous]`、`limit` 默认 5、范围 1–50、统一 `ApiResult`、缓存头按 design）
- [x] 2.3 （可选）为本地/测试环境提供种子公告数据或文档说明插入方式（集成测试 `ForumMeIntegrationFixture` 种子）

## 3. Model.Api：帖子列表 `sort=hot`

- [x] 3.1 在 `ForumPostsController.List` 解析 `sort`，校验 `latest`/`hot`，非法值返回 400
- [x] 3.2 `sort=hot` 时按 `LikeCount`、`CommentCount`、`ViewCount`、`CreatedAtUtc`、`Id` 排序，并与现有 `boardId`/`tag`/`q` 过滤组合；存在合法非空 `q` 时忽略 `sort`（与增量 spec 一致）
- [x] 3.3 确认 `Cache-Control` 与列表缓存语义与 hot 排序一致（必要时区分 no-store）（列表统一 `no-store`）

## 4. 自动化测试

- [x] 4.1 新增公告列表集成测试：空列表、limit 越界、有数据时字段与顺序
- [x] 4.2 新增帖子列表 `sort=hot` 集成测试：与 `sort=latest` 顺序差异、`boardId` 组合、`q` 存在时忽略 `sort`
- [x] 4.3 回归现有关键词搜索与限流相关测试

## 5. 前端首页右栏

- [x] 5.1 在 `frontend/src/api/clients.ts`（或等价层）增加 `getForumAnnouncements`、`getForumPosts` 带 `sort=hot` 的封装与类型
- [x] 5.2 更新 `frontend/src/views/HomeView.vue`：右栏「公告」「热门内容」请求真实 API，覆盖 loading / empty / error，热门条数与 `boardId` 与中间 Feed 对齐
- [x] 5.3 手动或 E2E 冒烟：桌面右栏三模块与窄屏折叠仍可用（沿用既有响应式栅格）

## 6. 收尾

- [x] 6.1 若网关或反向代理有路径白名单，更新配置与文档（`JIssWeb.Gateway.Api` 已有 `/api/forum/{**catch-all}`，无需改路由）
- [x] 6.2 `npm run pm:push` 前按需将 `scripts/github-sync/pm-plan.yaml` Issue #3 与实现对齐（实现合并后再执行）（Issue #3 已标 closed 并附落地说明）
