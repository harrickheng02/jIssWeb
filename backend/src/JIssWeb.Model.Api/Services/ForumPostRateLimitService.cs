using JIssWeb.Model.Api.Middleware;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumPostRateLimitService : IForumPostRateLimitService
{
    private readonly InProcessSlidingWindowRateLimiter _limiter;
    private readonly IOptions<ForumPostRateLimitOptions> _options;

    public ForumPostRateLimitService(
        InProcessSlidingWindowRateLimiter limiter,
        IOptions<ForumPostRateLimitOptions> options)
    {
        _limiter = limiter;
        _options = options;
    }

    public bool IsPostCreateRateLimited(string sub, string clientIp) =>
        IsRateLimited(PostKeys(sub, clientIp), _options.Value.MaxPosts, _options.Value.WindowSeconds);

    public void RecordSuccessfulPostCreate(string sub, string clientIp) =>
        RecordSuccess(PostKeys(sub, clientIp), _options.Value.MaxPosts, _options.Value.WindowSeconds);

    public bool IsReplyCreateRateLimited(string sub, string clientIp) =>
        IsRateLimited(ReplyKeys(sub, clientIp), _options.Value.MaxReplies, _options.Value.WindowSeconds);

    public void RecordSuccessfulReplyCreate(string sub, string clientIp) =>
        RecordSuccess(ReplyKeys(sub, clientIp), _options.Value.MaxReplies, _options.Value.WindowSeconds);

    private bool IsRateLimited(IEnumerable<string> keys, int maxRequests, int windowSeconds)
    {
        foreach (var key in keys)
        {
            if (_limiter.WouldExceed(key, maxRequests, windowSeconds))
                return true;
        }

        return false;
    }

    private void RecordSuccess(IEnumerable<string> keys, int maxRequests, int windowSeconds)
    {
        foreach (var key in keys)
            _limiter.TryConsume(key, maxRequests, windowSeconds);
    }

    private static IEnumerable<string> PostKeys(string sub, string clientIp)
    {
        yield return $"post:{sub}";
        yield return $"post:ip:{clientIp}";
    }

    private static IEnumerable<string> ReplyKeys(string sub, string clientIp)
    {
        yield return $"reply:{sub}";
        yield return $"reply:ip:{clientIp}";
    }
}
