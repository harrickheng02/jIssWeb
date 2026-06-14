## 1. 配置与 Options

- [x] 1.1 新增 `ForumBlockedWordsOptions`（`Enabled`、`Words[]`）与 `ForumPostRateLimitOptions`（`MaxPosts`、`MaxReplies`、`WindowSeconds`），在 `Program.cs` 绑定配置节
- [x] 1.2 在 `appsettings.json` 写入默认值（词表空、`Enabled: false` 或空词表等价；发帖 10/回复 30/60s）；在 `appsettings.Local.example.json` 补充 `_comment_BlockedWords` 与 `_comment_PostRateLimit` 单行示例

## 2. 屏蔽词过滤

- [x] 2.1 实现 `IForumBlockedWordFilter` / `ForumBlockedWordFilter`（大小写不敏感子串、多词 OR、命中返回 `(true, BLOCKED_CONTENT)`）
- [x] 2.2 在 `ForumPostsController.Create` 与 `CreateReply` 中于空字段校验之后调用 filter；命中返回 400 泛化 message
- [x] 2.3 集成测试 `ForumBlockedWordTests`：空词表通过、标题/正文/回复命中、message 不含命中词

## 3. 发帖频率限流

- [x] 3.1 提取或复用进程内滑动窗口 limiter（与 `ForumSearchIpRateLimiter` 同族）；注册 DI；`IForumPostRateLimitService` 仅在持久化成功后扣减
- [x] 3.2 在 `ForumPostsController.Create` / `CreateReply` 内检查配额（mute 与空字段之后、屏蔽词之前）；sub 主键 `post:{sub}` + IP 副键；超限 429 `RATE_LIMITED`
- [x] 3.3 移除 middleware 路径限流，改 controller/service 层（与 design 处理顺序一致）
- [x] 3.4 集成测试 `ForumPostRateLimitTests` + `ForumAntiSpamReviewFixTests`（429 优先、PUT 绕过、失败不扣减、禁言不扣减、搜索独立）

## 4. 回归与验证

- [x] 4.1 确认搜索限流测试仍绿：`ForumSearchRateLimitTests`
- [x] 4.2 运行 `dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj --filter "FullyQualifiedName~ForumBlockedWordTests|FullyQualifiedName~ForumPostRateLimitTests|FullyQualifiedName~ForumSearchRateLimitTests|FullyQualifiedName~ForumAntiSpamReviewFixTests"`

## 5. 归档准备

- [x] 5.1 change-review 对照 `forum-anti-spam-placeholder` 与 `forum-content-api` 增量；合并后 archive 并更新 pm-plan Issue #5
