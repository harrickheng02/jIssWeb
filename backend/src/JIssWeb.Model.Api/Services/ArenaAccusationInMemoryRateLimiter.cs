using System.Collections.Concurrent;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 进程内滑动窗口限流（按指认者）。默认每 60 秒最多 50 次，可按需注入配置。
/// 不依赖 Redis；命中 Allow=false 时控制器静默返回统一 200。
/// </summary>
public class ArenaAccusationInMemoryRateLimiter : IArenaAccusationRateLimiter
{
    private readonly int _maxPerWindow;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

    public ArenaAccusationInMemoryRateLimiter(int maxPerWindow = 50, int windowSeconds = 60)
    {
        _maxPerWindow = maxPerWindow;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    public bool Allow(string accuserId)
    {
        if (string.IsNullOrWhiteSpace(accuserId))
            return false;

        var now = DateTime.UtcNow;
        var queue = _hits.GetOrAdd(accuserId, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > _window)
                queue.Dequeue();
            if (queue.Count >= _maxPerWindow)
                return false;
            queue.Enqueue(now);
            return true;
        }
    }
}
