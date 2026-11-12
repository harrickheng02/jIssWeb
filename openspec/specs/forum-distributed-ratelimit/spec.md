## ADDED Requirements

### Requirement: Redis 滑动窗口限流后端

Model 服务 SHALL 提供 `IForumRateLimitBackend` 接口，定义 `TryConsumeAsync(key, max, windowSeconds)` 和 `WouldExceedAsync(key, max, windowSeconds)` 两个异步方法。`RedisRateLimitBackend` 实现 SHALL 使用 Redis Sorted Set + Lua 脚本实现真正的滑动窗口（`ZREMRANGEBYSCORE` 清除窗口外条目 → `ZCARD` 读取计数 → 条件 `ZADD` 追加 → `EXPIRE` 设置 TTL），每次操作原子完成。所有限流 key SHALL 以 `{KeyPrefix}rl:` 为前缀（例：`{prefix}rl:post:{sub}`），TTL SHALL 为 `windowSeconds * 2`。

#### Scenario: 同一 key 跨"实例"共享配额

- **WHEN** 两次对同一限流 key 的 `TryConsumeAsync` 调用使用同一 Redis 后端（不同连接对象模拟不同实例）
- **THEN** 第二次调用 SHALL 读取到包含第一次调用时间戳的 Sorted Set，跨调用共享计数

#### Scenario: 窗口内未超限时放行

- **WHEN** 当前窗口内对某 key 的消耗次数小于 `max`
- **THEN** `TryConsumeAsync` SHALL 返回 `true` 并向 Sorted Set 追加时间戳

#### Scenario: 窗口内达到上限后拒绝

- **WHEN** 当前窗口内对某 key 的消耗次数已达 `max`
- **THEN** `TryConsumeAsync` SHALL 返回 `false` 且不修改 Sorted Set

#### Scenario: 窗口过期后计数自动重置

- **WHEN** 某 key 的所有时间戳均早于 `now - windowSeconds`
- **THEN** `WouldExceedAsync` SHALL 返回 `false`（计数视为 0）

### Requirement: Fail-open — Redis 不可达时放行请求

当 `IConnectionMultiplexer` 为 null 或 Redis 操作抛出异常时，`RedisRateLimitBackend` SHALL 放行请求（返回 `true`）并写 `LogWarning`，不向调用方抛出异常。

#### Scenario: Redis 连接为 null 时放行

- **WHEN** `IConnectionMultiplexer` 注入值为 null
- **THEN** `TryConsumeAsync` SHALL 返回 `true`，请求继续处理
- **AND** SHALL NOT 抛出异常

#### Scenario: Redis 操作异常时放行

- **WHEN** `IConnectionMultiplexer` 非 null 但 `GetDatabase().ScriptEvaluateAsync` 抛出 `RedisException`
- **THEN** `TryConsumeAsync` SHALL 返回 `true` 并记录 `LogWarning`
- **AND** SHALL NOT 向控制器传播异常

### Requirement: 进程内降级后端

Model 服务 SHALL 保留 `InProcessRateLimitBackend`（基于现有 `ConcurrentDictionary<string, ConcurrentQueue<DateTime>>`）作为可选实现。当 `Forum:RateLimit:UseRedis` 为 `false` 时，DI 容器注册 `InProcessRateLimitBackend`；默认值为 `true`（使用 Redis）。

#### Scenario: UseRedis false 时使用进程内实现

- **WHEN** 配置 `Forum:RateLimit:UseRedis` 为 `false`
- **THEN** `IForumRateLimitBackend` 解析为 `InProcessRateLimitBackend`，限流行为与迁移前一致

### Requirement: AI 智能体账号独立配额命名空间

当经过身份验证的请求携带 JWT claim `accountType: agent` 时，Model 服务 SHALL 使用独立 Redis key 命名空间（`{prefix}rl:agent:post:{sub}`、`{prefix}rl:agent:reply:{sub}`），与人类用户 key 完全隔离。Agent 账号的帖子和回复最大请求数 SHALL 为人类配额乘以 `Forum:RateLimit:AgentPostMultiplier`（默认 `5`，可配置）。Agent 账号 SHALL 豁免搜索频率限制（搜索中间件直接放行，不写入任何限流 key）。

#### Scenario: Agent 账号使用独立 key

- **WHEN** JWT claim `accountType` 值为 `agent` 且用户发帖
- **THEN** 限流检查 SHALL 使用 key `{prefix}rl:agent:post:{sub}`，不读取或修改人类用户 key `{prefix}rl:post:{sub}`

#### Scenario: Agent 配额与人类配额互不干扰

- **WHEN** 人类用户已消耗完所有 `MaxPosts` 配额
- **THEN** Agent 账号仍 SHALL 能够成功发帖（配额独立）

#### Scenario: Agent 账号豁免搜索限流

- **WHEN** JWT claim `accountType` 值为 `agent` 且请求携带 `q` 参数
- **THEN** 搜索限流中间件 SHALL 不检查或消耗任何限流计数，直接放行

#### Scenario: Agent 超出独立配额时拒绝

- **WHEN** Agent 账号在窗口内发帖次数已达 `MaxPosts × AgentPostMultiplier`
- **THEN** 响应 SHALL 为 HTTP 429，error code `RATE_LIMITED`

### Requirement: 配置节 `Forum:RateLimit`

Model 服务 SHALL 支持配置节 `Forum:RateLimit`，字段包含：

- `UseRedis`（bool，默认 `true`）：控制 DI 注册 Redis 还是进程内实现。
- `AgentPostMultiplier`（int，默认 `5`）：agent 账号帖子/回复配额倍率。

现有 `Forum:PostRateLimit` 和 `Forum:SearchRateLimit` 节 SHALL 继续有效，字段命名不变。

#### Scenario: 缺省配置使用 Redis 后端

- **WHEN** `Forum:RateLimit:UseRedis` 未在配置中声明
- **THEN** 服务 SHALL 注册 `RedisRateLimitBackend`

#### Scenario: AgentPostMultiplier 缺省值生效

- **WHEN** `Forum:RateLimit:AgentPostMultiplier` 未配置
- **THEN** Agent 账号可用帖子配额 SHALL 为 `MaxPosts × 5`
