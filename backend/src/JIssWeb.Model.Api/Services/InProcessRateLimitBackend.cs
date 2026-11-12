using JIssWeb.Model.Api.Middleware;

namespace JIssWeb.Model.Api.Services;

public sealed class InProcessRateLimitBackend : IForumRateLimitBackend
{
    private readonly InProcessSlidingWindowRateLimiter _limiter;

    public InProcessRateLimitBackend(InProcessSlidingWindowRateLimiter limiter) => _limiter = limiter;

    public Task<bool> WouldExceedAsync(string key, int max, int windowSeconds) =>
        Task.FromResult(_limiter.WouldExceed(key, max, windowSeconds));

    public Task RecordAsync(string key, int windowSeconds)
    {
        _limiter.ForceConsume(key, windowSeconds);
        return Task.CompletedTask;
    }

    public Task<bool> TryConsumeAsync(string key, int max, int windowSeconds) =>
        Task.FromResult(_limiter.TryConsume(key, max, windowSeconds));
}
