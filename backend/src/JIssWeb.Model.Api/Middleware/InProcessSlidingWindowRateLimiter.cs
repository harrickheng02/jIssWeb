using System.Collections.Concurrent;

namespace JIssWeb.Model.Api.Middleware;

public sealed class InProcessSlidingWindowRateLimiter
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _buckets = new();

    public bool WouldExceed(string clientKey, int maxRequests, int windowSeconds)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-windowSeconds);
        var q = _buckets.GetOrAdd(clientKey, _ => new ConcurrentQueue<DateTime>());
        while (q.TryPeek(out var oldest) && oldest < windowStart)
            q.TryDequeue(out _);
        return q.Count >= maxRequests;
    }

    public bool TryConsume(string clientKey, int maxRequests, int windowSeconds)
    {
        if (WouldExceed(clientKey, maxRequests, windowSeconds))
            return false;
        var q = _buckets.GetOrAdd(clientKey, _ => new ConcurrentQueue<DateTime>());
        q.Enqueue(DateTime.UtcNow);
        return true;
    }

    public void ForceConsume(string clientKey, int windowSeconds)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-windowSeconds);
        var q = _buckets.GetOrAdd(clientKey, _ => new ConcurrentQueue<DateTime>());
        while (q.TryPeek(out var oldest) && oldest < windowStart)
            q.TryDequeue(out _);
        q.Enqueue(now);
    }
}
