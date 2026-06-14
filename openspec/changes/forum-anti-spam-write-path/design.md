## Context

Issue #5 为 `POST /api/forum/posts` 与 `POST /api/forum/posts/{id}/replies` 挂接了 `ForumBlockedWordFilter`（屏蔽词，`reject`/`local` 两种 Handling）和 `InProcessSlidingWindowRateLimiter`（`Forum:PostRateLimit`），但同期规范显式排除了三条写路径：

- `POST /api/forum/posts/drafts/{id}/publish`（`ForumDraftsController.PublishDraft`，无 `IForumBlockedWordFilter` / `IForumPostRateLimitService` 注入）
- `PUT /api/forum/posts/{postId}`（`ForumPostsController.UpdatePost`，`_blockedWords` 已注入但未调用）
- `PUT /api/forum/posts/{postId}/replies/{replyId}`（`ForumPostsController.UpdateReply`，同上）

Issue #23 合并后这三条路径成为绕路入口。本 change 仅在后端做最小扩展，不涉及前端与新配置键。

## Goals / Non-Goals

**Goals:**

- `PublishDraft`：注入过滤器和限流服务；publish 前校验屏蔽词和配额；成功 publish 后增量计数。
- `UpdatePost` / `UpdateReply`：在现有鉴权与 mute 拦截后插入屏蔽词校验，命中则 400。
- 集成测试覆盖上述三条路径的绕路回归场景。

**Non-Goals:**

分布式限流（#26）；屏蔽词 Mongo 化（#29）；CAPTCHA（#27）；行为降速（#28）；mod / admin 端点；前端改动；新配置节。

## Decisions

### 决策 A：publish 共用 `post:{sub}` 发帖配额

**选项**：

1. 共用现有 `IsPostCreateRateLimited` / `RecordSuccessfulPostCreate` 计数键（`post:{sub}`、`post:ip:{ip}`）。
2. 为 publish 引入独立配额键（如 `publish:{sub}`）。

**选择**：选项 1。publish 在用户视角等同于「发帖」，共用配额可防止「反复草稿-publish」绕过发帖频率上限，且无需新增配置。  

**后续**：Issue #26 迁移到 Redis 时 publish 计数键随同迁移，不需要二次更改。

### 决策 B：PUT 自编辑中 `local` Handling 统一走 reject

`local` 模式的语义是「命中词不入库，本地假成功」，专为 `POST` 创建设计。PUT 编辑的内容已存库（draft/published），若 PUT 命中屏蔽词而返回伪成功，旧内容不变、前端却认为已更新，会造成状态不一致。因此：

- `PUT` 端点一律调用 `_blockedWords.Evaluate()` → `Reject` / `Local` 均返回 400 `BLOCKED_CONTENT`（不区分 Handling，仅 `Pass` 放行）。
- 代码实现可简化为 `if (_blockedWords.IsBlocked(title, body)) return BadRequest(...)` 或调用 `Evaluate()` 后对非 Pass 值统一处理。

### 决策 C：publish 的校验顺序

现有 `POST` 创建路径的实现顺序（屏蔽词先、限流后）与规范定义（限流先、屏蔽词后）不一致。为降低 diff 复杂度并与现有行为一致，publish 采用**与 reply create 相同顺序**：

1. auth + mute（已由 `[BlockForumMuted]` attribute 处理）
2. draft 加载与所有权校验
3. 必填字段校验（title/body 非空、boardId 有效）
4. 屏蔽词校验（命中 → 400 或 local，publish 场景 local 行为见下）
5. 限流校验（超限 → 429）
6. `State` 翻转 + Mongo Update
7. 计数器增量

**publish 的 `local` 处理**：publish 不是新建，无法返回 `local:` 前缀 ID。命中屏蔽词时统一返回 400 `BLOCKED_CONTENT`（与决策 B 一致），不论 `Handling` 配置。此行为在规范增量中明确注明。

### 决策 D：`ForumDraftsController` 注入粒度

`ForumDraftsController` 当前只在 `PublishDraft` 方法中需要过滤器和限流；草稿 CRUD（create/update/delete）不需要（草稿不公开，屏蔽词检查在 publish 时触发已足够）。因此：注入 `IForumBlockedWordFilter` 与 `IForumPostRateLimitService`，仅在 `PublishDraft` 中调用。

## Risks / Trade-offs

- **[风险] 现有集成测试无 publish/PUT 屏蔽词场景** → 本 change 集中补测，确保 CI 门禁覆盖。
- **[取舍] publish 计数与 create 共用配额** → 如后续产品需要区分「当天发帖数」与「当天 publish 数」，须拆分键；目前不需要。
- **[取舍] PUT local 统一走 reject** → 产品一致性优先（PUT 告知用户内容含违规词），暂不支持「静默不更新」行为。

## Migration Plan

无数据迁移，无破坏性 API 变更。部署步骤：

1. 合并 PR 后标准 rolling restart 即可生效。
2. 回滚：还原 PR 即可，无持久化副作用。

## Open Questions

无。
