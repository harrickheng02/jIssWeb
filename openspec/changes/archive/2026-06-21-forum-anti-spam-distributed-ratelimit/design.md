## Context

Model.Api 当前的限流计数器由 `InProcessSlidingWindowRateLimiter`（`ConcurrentDictionary<string, ConcurrentQueue<DateTime>>`）实现，注册为全局单例。单实例下行为正确；一旦水平扩展（多 Pod），每个进程持有独立计数桶，同一用户可在不同实例上各自消耗完整配额，限流形同虚设。

Redis（StackExchange.Redis）已在 Model.Api 中就位：`Program.cs` 注册了 `IConnectionMultiplexer?`，`ForumEngagementLikeCountCache` 已建立 null-guard + try/catch 的 fail-open 模式，`RedisSettings`（`KeyPrefix`、`ConnectionString`）统一管理前缀。本次变更复用已有基础设施，不引入新依赖。

## Goals / Non-Goals

**Goals:**

- 帖子创建、草稿发布、回复创建、关键词搜索四条路径的限流计数器迁移到 Redis，多实例共享同一配额。
- 图灵场 AI 智能体账号（`accountType: agent` JWT claim）使用独立 Redis key 命名空间，不与人类用户共享配额，且人类限流降速不影响授权 AI 账号。
- Redis 不可达时 fail-open（请求放行 + 警告日志），保持与 `ForumEngagementLikeCountCache` 一致的容错策略。

**Non-Goals:**

- CAPTCHA 接入（change-C #27）。
- 行为信号降速（新账号严格配额 + 重复正文哈希检测，change-D #28）。
- 屏蔽词 Mongo 化（change-E #29）。
- Gateway 层全局限流或跨服务统一限流中台。
- 非 Model.Api 服务的限流迁移。

## Decisions

### 决策 1：Redis 滑动窗口算法 — Sorted Set + Lua 脚本

**选择**：每个限流 key 对应一个 Redis Sorted Set，member 为请求时间戳（纳秒），score 同值。以 Lua 脚本原子执行：`ZREMRANGEBYSCORE`（清除窗口外记录）→ `ZCARD`（读取当前计数）→ 按条件 `ZADD`（追加新记录）→ `EXPIRE`（维持 TTL）。

**理由**：真正的滑动窗口，无固定窗口边界的双倍突发问题；Lua 单次往返保证原子性；TTL 自动清理过期 key，不存在内存泄漏。

**备选**：`INCR` + `EXPIRE` 固定窗口——实现更简单，但窗口边界处允许约 2× 突发；因图灵场 AI 配额精度要求，弃用。

### 决策 2：抽象接口 `IForumRateLimitBackend`

**选择**：引入 `IForumRateLimitBackend`，提供 `TryConsumeAsync(key, max, windowSeconds)` 和 `WouldExceedAsync(key, max, windowSeconds)` 两个方法。提供两个实现：`RedisRateLimitBackend`（生产）和 `InProcessRateLimitBackend`（降级/测试）。通过 `Forum:RateLimit:UseRedis`（bool，默认 `true`）在 DI 层切换。

**理由**：现有 `InProcessSlidingWindowRateLimiter` 测试中已被直接注入，抽象后两套实现可独立测试；降级时无需改代码，仅改配置。

**备选**：直接替换 `InProcessSlidingWindowRateLimiter`——更简洁，但 Redis 不可用时无法自动降级，集成测试需 Redis 容器，本地开发门槛升高。

### 决策 3：Agent 命名空间通过 JWT claim 判断，不查 DB

**选择**：在限流服务层读取 `HttpContext.User` 的 `accountType` claim（字符串 `agent`）。若为 agent，key 前缀改为 `agent:`（例：`agent:post:{sub}`）；最大请求数乘以 `Forum:RateLimit:AgentPostMultiplier`（默认 5）。

**理由**：JWT claim 已在 Bearer 中间件验证，无额外 IO；agent 配额独立命名，允许单独调整而不影响人类配额。

**备选**：查 User.Api 获取 accountType——增加一次同步 HTTP 调用，限流路径引入网络依赖，违背 fail-open 原则。

### 决策 4：搜索限流统一归入 `IForumPostRateLimitService`

**选择**：`ForumSearchRateLimitMiddleware` 改为依赖 `IForumRateLimitBackend`，不再注入 `InProcessSlidingWindowRateLimiter`。搜索 key 格式：`search:ip:{ip}`（agent 账号豁免搜索限流）。

**理由**：保持单一后端实现，避免"中间件走 Redis、服务走内存"的双套维护负担。

## Risks / Trade-offs

**[风险] Redis key 命名冲突** → 所有限流 key 统一加 `{RedisSettings.KeyPrefix}rl:` 前缀（例：`{prefix}rl:post:{sub}`），与 `forum:lc:` 点赞缓存命名空间隔离。

**[风险] 单 Redis 故障放大** → Fail-open：`IConnectionMultiplexer?` 为 null 或操作抛异常时，直接放行请求并写 `LogWarning`；与 `ForumEngagementLikeCountCache` 行为对称，不新增风险面。

**[取舍] Sorted Set 内存占用** → 每个活跃 key 在窗口期内存储请求时间戳序列。默认窗口 60s，MaxPosts=10，单 key 最多 10 个 `double` entry（约 200 字节）；所有活跃用户合计可控。TTL 设为 `WindowSeconds * 2` 自动清理。

**[取舍] 计数器在部署时重置** → 进程内计数器本就会在重启时清零，迁移到 Redis 后计数在实例间共享且跨部署持久。这实际上是改善，不是问题。

## Migration Plan

1. 添加 `IForumRateLimitBackend` 接口及 `RedisRateLimitBackend` / `InProcessRateLimitBackend` 实现。
2. 更新 `ForumPostRateLimitService` 依赖 `IForumRateLimitBackend`；更新 `ForumSearchRateLimitMiddleware` 同理。
3. `Program.cs` 按 `Forum:RateLimit:UseRedis` 注册对应实现；默认注册 `RedisRateLimitBackend`（复用已有 `IConnectionMultiplexer?`）。
4. `appsettings.Local.example.json` 新增 `Forum:RateLimit` 节占位（`UseRedis: true`, `AgentPostMultiplier: 5`）。
5. 集成测试：新增 `RateLimitRedisIntegrationFixture`，使用 Testcontainers Redis；覆盖跨"实例"配额共享（同 key 两次调用）、Redis 断连 fail-open、agent key 独立性三条主路径。
6. 回滚：`Forum:RateLimit:UseRedis: false` 即切回进程内实现，无数据迁移需要。

## Open Questions

- `AgentPostMultiplier` 的合理默认值待图灵场首期调度系统上线后根据实际发帖频率校准（当前 5× 为占位估算）。
- 如果未来需要 Gateway 层全局限流，本次 Redis key 格式应保持与 YARP 插件的 key 约定兼容——留待 change-B 实施后在 Gateway change 中评估。
