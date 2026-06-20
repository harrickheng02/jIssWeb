## 1. 接口抽象与配置

- [x] 1.1 新建 `IForumRateLimitBackend` 接口（`TryConsumeAsync`、`WouldExceedAsync`），替换 `InProcessSlidingWindowRateLimiter` 的直接注入
- [x] 1.2 新增 `ForumRateLimitOptions`（`UseRedis: true`、`AgentPostMultiplier: 5`），绑定 `Forum:RateLimit` 配置节，并更新 `appsettings.Local.example.json`
- [x] 1.3 将现有 `InProcessSlidingWindowRateLimiter` 包装为 `InProcessRateLimitBackend`（实现 `IForumRateLimitBackend`）

## 2. Redis 后端实现

- [x] 2.1 编写 `RedisRateLimitBackend`：Sorted Set + Lua 脚本滑动窗口（`ZREMRANGEBYSCORE` → `ZCARD` → 条件 `ZADD` → `EXPIRE`）
- [x] 2.2 在 `RedisRateLimitBackend` 中实现 fail-open：`IConnectionMultiplexer?` 为 null 或操作抛异常时返回 `true` 并写 `LogWarning`
- [x] 2.3 所有 key 统一加 `{KeyPrefix}rl:` 前缀；TTL 设为 `windowSeconds * 2`
- [x] 2.4 在 `Program.cs` 按 `Forum:RateLimit:UseRedis` 注册 `RedisRateLimitBackend` 或 `InProcessRateLimitBackend`

## 3. Agent 账号命名空间

- [x] 3.1 在 `ForumPostRateLimitService` 中读取 `HttpContext.User` 的 `accountType` claim；若为 `agent`，生成 `agent:post:{sub}` / `agent:reply:{sub}` key，限额乘以 `AgentPostMultiplier`
- [x] 3.2 在 `ForumSearchRateLimitMiddleware` 中检查 `accountType: agent`，命中则跳过计数直接放行

## 4. 服务层更新

- [x] 4.1 更新 `ForumPostRateLimitService` 依赖 `IForumRateLimitBackend`（替换 `InProcessSlidingWindowRateLimiter`），方法改为 `async`
- [x] 4.2 更新 `ForumSearchRateLimitMiddleware` 依赖 `IForumRateLimitBackend`（替换直接注入的 `InProcessSlidingWindowRateLimiter`）
- [x] 4.3 更新 `IForumPostRateLimitService` 接口方法签名为 `async`（`IsPostCreateRateLimitedAsync` / `RecordSuccessfulPostCreateAsync` 等），并同步更新调用方（`ForumPostsController`、`ForumDraftsController`）

## 5. 集成测试

- [x] 5.1 新增 `RateLimitRedisIntegrationFixture`（Testcontainers Redis），配置 `Forum:RateLimit:UseRedis: true`
- [x] 5.2 测试：同一 key 两次 `TryConsumeAsync` 调用后计数共享（验证跨"实例"语义）
- [x] 5.3 测试：Redis 断连（`IConnectionMultiplexer` mock 抛异常）时 fail-open，请求正常放行
- [x] 5.4 测试：agent 账号发帖使用 `agent:post:{sub}` key，不影响同 `sub` 的人类用户计数
- [x] 5.5 测试：agent 账号搜索请求（携带 `q`）不消耗搜索配额，人类用户请求正常计数

## 6. 验收与收尾

- [x] 6.1 运行 `dotnet test tests/JIssWeb.Model.Api.Tests`，所有测试绿灯
- [x] 6.2 本地启动 `docker compose up -d redis` + `dotnet run --project JIssWeb.Model.Api`，手动验证发帖 429 行为与 Redis key 写入（`redis-cli monitor`）
- [x] 6.3 检查 `InProcessSlidingWindowRateLimiter` 是否仍有直接注入残留，若无则可保留类体（供 `InProcessRateLimitBackend` 内部复用）或内联删除
