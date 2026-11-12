using JIssWeb.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Services;

public sealed class RedisRateLimitBackend : IForumRateLimitBackend
{
    // Sorted Set sliding window: remove expired entries, check count, conditionally add.
    // KEYS[1]=key ARGV[1]=nowMs ARGV[2]=windowStartMs ARGV[3]=max ARGV[4]=ttlSeconds ARGV[5]=member
    private const string ConsumeScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[2])
        local count = redis.call('ZCARD', KEYS[1])
        if count < tonumber(ARGV[3]) then
            redis.call('ZADD', KEYS[1], ARGV[1], ARGV[5])
            redis.call('EXPIRE', KEYS[1], tonumber(ARGV[4]))
            return 1
        else
            return 0
        end
        """;

    // Check only — no ZADD.
    // KEYS[1]=key ARGV[1]=windowStartMs ARGV[2]=max
    private const string WouldExceedScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
        local count = redis.call('ZCARD', KEYS[1])
        if count >= tonumber(ARGV[2]) then
            return 1
        else
            return 0
        end
        """;

    // Unconditional record — always adds.
    // KEYS[1]=key ARGV[1]=nowMs ARGV[2]=windowStartMs ARGV[3]=ttlSeconds ARGV[4]=member
    private const string RecordScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[2])
        redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4])
        redis.call('EXPIRE', KEYS[1], tonumber(ARGV[3]))
        """;

    private readonly IConnectionMultiplexer? _mux;
    private readonly string _keyPrefix;
    private readonly ILogger<RedisRateLimitBackend> _logger;

    public RedisRateLimitBackend(
        IConnectionMultiplexer? mux,
        IOptions<RedisSettings> redisOptions,
        ILogger<RedisRateLimitBackend> logger)
    {
        _mux = mux;
        _keyPrefix = (redisOptions.Value.KeyPrefix ?? "") + "rl:";
        _logger = logger;
    }

    public async Task<bool> WouldExceedAsync(string key, int max, int windowSeconds)
    {
        if (_mux is null) return false;
        try
        {
            var db = _mux.GetDatabase();
            var windowStartMs = NowMs() - windowSeconds * 1000L;
            var result = (int)await db.ScriptEvaluateAsync(WouldExceedScript,
                keys: [_keyPrefix + key],
                values: [(RedisValue)windowStartMs, (RedisValue)max]);
            return result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis WouldExceed failed for key {Key}; fail-open", key);
            return false;
        }
    }

    public async Task RecordAsync(string key, int windowSeconds)
    {
        if (_mux is null) return;
        try
        {
            var db = _mux.GetDatabase();
            var nowMs = NowMs();
            var windowStartMs = nowMs - windowSeconds * 1000L;
            var ttl = windowSeconds * 2;
            var member = Guid.NewGuid().ToString("N");
            await db.ScriptEvaluateAsync(RecordScript,
                keys: [_keyPrefix + key],
                values: [(RedisValue)nowMs, (RedisValue)windowStartMs, (RedisValue)ttl, (RedisValue)member]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis Record failed for key {Key}", key);
        }
    }

    public async Task<bool> TryConsumeAsync(string key, int max, int windowSeconds)
    {
        if (_mux is null) return true;
        try
        {
            var db = _mux.GetDatabase();
            var nowMs = NowMs();
            var windowStartMs = nowMs - windowSeconds * 1000L;
            var ttl = windowSeconds * 2;
            var member = Guid.NewGuid().ToString("N");
            var result = (int)await db.ScriptEvaluateAsync(ConsumeScript,
                keys: [_keyPrefix + key],
                values: [(RedisValue)nowMs, (RedisValue)windowStartMs, (RedisValue)max, (RedisValue)ttl, (RedisValue)member]);
            return result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis TryConsume failed for key {Key}; fail-open", key);
            return true;
        }
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
