## Context

Model.Api 已有 `ForumPostsController.Create` / `CreateReply`、`BlockForumMuted` 禁言拦截，以及 Issue #15 交付的 `ForumSearchIpRateLimiter` + `ForumSearchRateLimitMiddleware`（仅 `GET /api/forum/posts?q=`）。Issue #5 需在相同服务内增加内容入库前的屏蔽词 gate 与用户写操作频率限制，配置与搜索限流隔离。

## Goals / Non-Goals

**Goals:**

- `Forum:BlockedWords:Enabled` + `Words[]` 静态词表；空表或未启用时零行为差异。
- 发帖扫描 `title` + `body`；回复扫描 `body`；多词 OR、大小写不敏感子串；400 `BLOCKED_CONTENT`，message 泛化。
- `Forum:PostRateLimit`：发帖 10/60s、回复 30/60s（可配置），按 JWT `sub` 计数；同 IP 副键复用与搜索相同的 `X-Forwarded-For` 解析，防 token 轮换。
- 仅覆盖 `POST /api/forum/posts`（精确路径，不含 `/drafts`）与 `POST /api/forum/posts/{id}/replies`。
- 集成测试覆盖屏蔽词命中/未命中、发帖/回复配额 429、与搜索限流配置隔离。

**Non-Goals:**

- 草稿 CRUD、`POST .../drafts/{id}/publish`、自编辑 PUT 的过滤或限流。
- Mongo 词表、运营 UI、热加载（重启加载配置即可）。
- CAPTCHA、分布式限流、LLM 审核、谐音/变体检测。

## Decisions

1. **词表来源**：`appsettings` 静态 `string[]`（MVP）；后续迭代切 Mongo 并与标签后台统一。**备选** Mongo 首期：过重，defer。

2. **过滤挂钩**：`IForumBlockedWordFilter` service，在 `Create` / `CreateReply` 内于空字段校验之后、board/tags/锁帖之前调用。**备选** middleware：需重复解析 body，测试成本高；**选用 controller/service 层**。

3. **限流实现**：`IForumPostRateLimitService` + 共享 `InProcessSlidingWindowRateLimiter`；在 `Create` / `CreateReply` 内于 mute 与空字段校验之后、屏蔽词之前检查配额；**仅在 Insert 成功后**扣计数。主键 `post:{sub}` / `reply:{sub}`，副键 `post:ip:{ip}` / `reply:ip:{ip}`。

4. **处理顺序**：鉴权 → `BlockForumMuted` → 空字段校验 → **限流检查** → 屏蔽词 → 业务校验 → 持久化 → **限流扣减**。

5. **错误码**：屏蔽词 `BLOCKED_CONTENT`（400）；限流 `RATE_LIMITED`（429），与搜索/Auth 一致。

6. **配置文档**：`appsettings.json` 写默认值；`appsettings.Local.example.json` 用 `_comment_BlockedWords` / `_comment_PostRateLimit` 注释单行示例。

## Risks / Trade-offs

- **[Risk]** 进程内 limiter 多实例部署配额不一致 → **Mitigation**：与 #15 相同假设；分布式 defer。
- **[Risk]** 子串误杀正常词 → **Mitigation**：词表可空；运营自行维护；不回显命中词。
- **[Risk]** publish/PUT 绕过屏蔽 → **Mitigation**：spec 非目标明确；后续单开 Issue。
- **[Risk]** IP 副键误伤 NAT → **Mitigation**：配额按环境调优；sub 为主路径。

## Migration Plan

- 部署：先上后端（词表默认空、限流默认开启），无数据迁移。
- 回滚：移除 middleware 与 filter 调用；配置节可保留。

## Open Questions

- 无（explore 阶段已拍板）。
