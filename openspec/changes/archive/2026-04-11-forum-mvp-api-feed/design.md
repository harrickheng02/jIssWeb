## Context

首页 `forum-homepage-shell` 已实现；`JIssWeb.Model.Api` 已有 Mongo/Redis 注册与 JWT 校验骨架；网关已转发 `auth`、`customers`、`profile`、`bff`。论坛领域尚无 Controller 与集合；前端 `HomeView` 使用本地假数据。

## Goals / Non-Goals

**Goals:**

- 端到端：网关 → Model.Api → Mongo → 前端 Feed/详情/发帖/回复。
- 身份：写操作从 JWT 取 `sub` 作为作者标识，符合 `token-identity-consistency`。
- 公开读、登录写；分页与错误形状与现有 API 习惯一致（若项目已有 `ApiResult` 则复用）。

**Non-Goals:**

- 搜索、通知、附件、富文本、点赞/浏览真实计数、BFF 编排、版区/标签后台管理。

## Decisions

1. **宿主**：论坛 REST 放在 **Model.Api（5099）**，与「内容/模型」域一致；User 仅鉴权，不承载帖子。
2. **URL**：对外统一 **`/api/forum/...`**（与网关 `ReverseProxy` 新增 route 一致）；前端经 **`/api`** 代理到网关，不直连端口。
3. **持久化**：**MongoDB** 集合（如 `posts`、`replies`）；帖子 ID 用字符串或 ObjectId 对外序列化为统一 id 字段；MVP 回复可平铺挂 `postId`。
4. **作者展示**：响应含 `authorId`（= `sub`）；**显示名**可延后：首版可省略或占位字符串，避免阻塞闭环（可选后续读 Customer Profile）。
5. **计数**：列表卡片需 like/comment/view 字段——MVP 可 **返回数字字段** 发帖时默认 0，回复创建时 **commentCount +1**；浏览/点赞可固定 0 或简单 `Inc`。
6. **网关**：新增 **cluster** 指向 `http://localhost:5099/`（与 docker 环境变量对齐），**route** `Path` 匹配 `/api/forum/{**catch-all}`，与其它路由相同 **Bearer 透传**。

**备选未选**：新建独立 Forum 微服务（MVP 过重）；论坛挂在 `JIssWeb.Api`（5096）但未进网关当前配置，需额外拉齐 compose。

## Risks / Trade-offs

- **[Risk]** Model 服务职责变重 → **缓解**：接口边界清晰，后续可拆服务不改路径（网关切换 cluster）。
- **[Risk]** 作者显示名与 Profile 不一致 → **缓解**：先 `sub` 或占位，规范在 `forum-content-api` 写明。
- **[Risk]** 本地与 Docker 上游端口不一致 → **缓解**：网关配置走环境变量或 `appsettings.Development.json` 文档化。

## Migration Plan

1. 部署顺序：先 Model 与索引 → 再网关配置 → 最后前端切换。
2. 回滚：网关去掉 forum route；前端回退 mock；Mongo 数据可保留。

## Open Questions

- 帖子「摘要」由客户端提交还是服务端从正文截取（建议服务端截取以满足卡片一致性）。
- 列表筛选 `latest|hot|featured` MVP 是否仅 `createdAt` 排序，热门/精华占位。
