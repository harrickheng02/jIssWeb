using System.Collections.Concurrent;

namespace JIssWeb.Model.Api.Middleware;

public sealed class ForumSearchIpRateLimiter
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _buckets = new();

    public bool TryConsume(string clientKey, int maxRequests, int windowSeconds)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-windowSeconds);
        var q = _buckets.GetOrAdd(clientKey, _ => new ConcurrentQueue<DateTime>());
        while (q.TryPeek(out var oldest) && oldest < windowStart)
            q.TryDequeue(out _);
        if (q.Count >= maxRequests)
            return false;
        q.Enqueue(now);
        return true;
    }
}
