## 1. ForumDraftsController — 注入依赖并补充失败测试

- [x] 1.1 在 `ForumDraftsController` 构造函数中注入 `IForumBlockedWordFilter` 和 `IForumPostRateLimitService`（参照 `ForumPostsController` 的注入方式）
- [x] 1.2 在 `ForumBlockedWordTests`（或新建 `ForumBlockedWordWritePathTests`）中先写失败测试：publish 帖子标题含屏蔽词应返回 400 `BLOCKED_CONTENT`
- [x] 1.3 在 `ForumBlockedWordTests` 中写失败测试：publish 帖子正文含屏蔽词应返回 400 `BLOCKED_CONTENT`
- [x] 1.4 在 `ForumPostRateLimitTests` 中写失败测试：用户达到 `MaxPosts` 后 publish 应返回 429 `RATE_LIMITED`
- [x] 1.5 在 `ForumPostRateLimitTests` 中写失败测试：publish 成功后配额计数器应增量（再次 publish 超限后 429）

## 2. ForumDraftsController — 实现 PublishDraft 校验

- [x] 2.1 在 `PublishDraft` 方法的必填字段校验之后、Mongo state 翻转之前，插入屏蔽词校验：调用 `_blockedWords.Evaluate(draft.Title, draft.Body)`；任何非 `Pass` 结果均返回 400 `BLOCKED_CONTENT`（不区分 Handling 配置）
- [x] 2.2 在屏蔽词校验通过后插入限流校验：调用 `_postRateLimit.IsPostCreateRateLimited(sub, ip)`；超限返回 429 `RATE_LIMITED`
- [x] 2.3 在 Mongo update（State 翻转为 published）成功后，调用 `_postRateLimit.RecordSuccessfulPostCreate(sub, ip)` 增量计数
- [x] 2.4 运行 1.2–1.5 的集成测试，确认全部转为绿色

## 3. ForumPostsController — UpdatePost（PUT 帖子自编辑）失败测试与实现

- [x] 3.1 在测试文件中写失败测试：PUT 帖子时 title 含屏蔽词应返回 400（`Handling: reject`）
- [x] 3.2 写失败测试：PUT 帖子时 body 含屏蔽词应返回 400（`Handling: local`，PUT 场景同样 reject）
- [x] 3.3 写失败测试：PUT 仅更新 tags 时，已存储 title/body 含屏蔽词不应被拦截（只评估请求体中提交的字段）
- [x] 3.4 在 `UpdatePost` 方法中，在 tags 归一化之后、Mongo update 之前，插入屏蔽词校验：仅对请求体中非空的 `title` 和/或 `body` 调用 `_blockedWords.IsBlocked()`（或分别传 null 给 `Evaluate`）；命中时返回 400 `BLOCKED_CONTENT`
- [x] 3.5 运行 3.1–3.3 的集成测试，确认全部转为绿色

## 4. ForumPostsController — UpdateReply（PUT 回复自编辑）失败测试与实现

- [x] 4.1 写失败测试：PUT 回复时 body 含屏蔽词应返回 400 `BLOCKED_CONTENT`
- [x] 4.2 写失败测试：PUT 回复 body 干净时正常返回 200
- [x] 4.3 在 `UpdateReply` 方法中，在 body 非空校验之后、Mongo update 之前，插入 `_blockedWords.Evaluate(null, request.Body)`；非 Pass 返回 400 `BLOCKED_CONTENT`
- [x] 4.4 运行 4.1–4.2 的集成测试，确认全部转为绿色

## 5. 回归验证与构建

- [x] 5.1 运行完整后端测试套件：`dotnet test backend/tests/JIssWeb.Model.Api.Tests` — 确认无退化
- [x] 5.2 运行前端测试（本 change 无前端改动，作为对照基准）：`cd frontend && npm test`
- [x] 5.3 执行 `change-review`，对照 `specs/forum-anti-spam-placeholder/spec.md` 增量中的所有 `SHALL` 逐条核查实现
