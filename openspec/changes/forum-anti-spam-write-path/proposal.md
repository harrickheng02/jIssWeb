## Why

Issue #23 交付了草稿 publish（`POST /api/forum/posts/drafts/{id}/publish`）与作者 PUT 自编辑（帖 title/body/tags、回复 body），但 `forum-anti-spam-placeholder`（Issue #5）的「Out of scope」条款显式排除了这两条写路径，导致屏蔽词过滤与发帖频率限流可被绕过——用户可先以任意内容发为草稿，再 publish 或通过 PUT 改写内容，完全跳过 `ForumBlockedWordFilter` 与 `InProcessSlidingWindowRateLimiter`。

## What Changes

- `POST /api/forum/posts/drafts/{id}/publish`：接入与 `POST /api/forum/posts` 同级的 `ForumBlockedWordFilter`（屏蔽词命中 → 400 `BLOCKED_CONTENT` 或 local 响应，策略与现有 `Forum:BlockedWords:Handling` 一致）；纳入发帖频率限流，**与 `POST /api/forum/posts` 共用** `post:{sub}` / `post:ip:{ip}` 计数器与 `MaxPosts` 配额（publish 视为「发帖」而非独立操作，共用配额防止反复草稿+发布绕限）。
- `PUT /api/forum/posts/{postId}`（title/body/tags 自编辑）：接入 `ForumBlockedWordFilter`，命中时行为与 `Handling` 当前配置一致（`reject` → 400；`local` → 本 PUT 场景无「本地化」语义，统一返回 400 `BLOCKED_CONTENT` 且不更新，设计文档说明此取舍）。
- `PUT /api/forum/posts/{postId}/replies/{replyId}`（reply body 自编辑）：接入 `ForumBlockedWordFilter`，同上处理。
- `forum-anti-spam-placeholder` 的「Out of scope」条款删除上述三条例外；修改规范场景覆盖 publish/PUT 路径。

## Capabilities

### New Capabilities

（无，本 change 仅扩展现有规范覆盖范围。）

### Modified Capabilities

- `forum-anti-spam-placeholder`：移除对 draft publish 与作者 PUT 自编辑的豁免；新增 publish 共用发帖配额场景；新增 PUT 屏蔽词拦截场景（`local` 模式下 PUT 统一走 reject 语义的设计说明）。

## Impact

- **Model.Api**：`ForumDraftsController.Publish`、`ForumPostsController.Put`、回复自编辑端点各插入 `ForumBlockedWordFilter` 与（publish 场景）`InProcessSlidingWindowRateLimiter`；无新配置键，复用 `Forum:BlockedWords` 与 `Forum:PostRateLimit`。
- **集成测试**：`ForumBlockedWordTests` 增补 publish/PUT 回归用例；`ForumPostRateLimitTests` 增补 publish 共用配额用例。
- **服务边界**：仅 `JIssWeb.Model.Api`；User/Gateway/BFF/前端无变更。
- **非目标**：分布式限流（Issue #26）；屏蔽词 Mongo 化（Issue #29）；CAPTCHA（Issue #27）；行为降速（Issue #28）；任何 mod/admin 端点；版主软删/硬删路径。
