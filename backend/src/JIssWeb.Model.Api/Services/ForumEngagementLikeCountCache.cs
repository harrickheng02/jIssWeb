using JIssWeb.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumEngagementLikeCountCache
{
    private readonly IConnectionMultiplexer? _mux;
    private readonly RedisSettings _redis;
    private readonly ILogger<ForumEngagementLikeCountCache> _logger;

    public ForumEngagementLikeCountCache(
        IConnectionMultiplexer? mux,
        IOptions<RedisSettings> redisOptions,
        ILogger<ForumEngagementLikeCountCache> logger)
    {
        _mux = mux;
        _redis = redisOptions.Value;
        _logger = logger;
    }

    public bool IsEnabled => _mux is not null && !string.IsNullOrWhiteSpace(_redis.ConnectionString);

    private RedisKey Key(string postId) => $"{_redis.KeyPrefix}forum:lc:{postId}";

    public async Task SetLikeCountAsync(string postId, int value)
    {
        if (_mux is null) return;
        try
        {
            var db = _mux.GetDatabase();
            await db.StringSetAsync(Key(postId), value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET likeCount for post {PostId}", postId);
        }
    }

    public async Task RemoveLikeCountAsync(string postId)
    {
        if (_mux is null) return;
        try
        {
            await _mux.GetDatabase().KeyDeleteAsync(Key(postId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis DEL likeCount for post {PostId}", postId);
        }
    }

    public async Task<IReadOnlyDictionary<string, int>> GetManyAsync(IReadOnlyList<string> postIds)
    {
        if (_mux is null || postIds.Count == 0)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            var db = _mux.GetDatabase();
            var keys = postIds.Select(id => Key(id)).ToArray();
            var vals = await db.StringGetAsync(keys).ConfigureAwait(false);
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < postIds.Count; i++)
            {
                if (vals[i].HasValue && int.TryParse(vals[i], out var n))
                    map[postIds[i]] = n;
            }

            return map;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis MGET likeCount");
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    public async Task<int?> GetAsync(string postId)
    {
        if (_mux is null) return null;
        try
        {
            var v = await _mux.GetDatabase().StringGetAsync(Key(postId)).ConfigureAwait(false);
            if (v.HasValue && int.TryParse(v, out var n)) return n;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET likeCount for post {PostId}", postId);
            return null;
        }
    }
}
