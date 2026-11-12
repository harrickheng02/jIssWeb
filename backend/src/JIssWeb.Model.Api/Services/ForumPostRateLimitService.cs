using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumPostRateLimitService : IForumPostRateLimitService
{
    private readonly IForumRateLimitBackend _backend;
    private readonly IOptions<ForumPostRateLimitOptions> _postOpts;
    private readonly IOptions<ForumRateLimitOptions> _rateLimitOpts;

    public ForumPostRateLimitService(
        IForumRateLimitBackend backend,
        IOptions<ForumPostRateLimitOptions> postOpts,
        IOptions<ForumRateLimitOptions> rateLimitOpts)
    {
        _backend = backend;
        _postOpts = postOpts;
        _rateLimitOpts = rateLimitOpts;
    }

    public Task<bool> IsPostCreateRateLimitedAsync(string sub, string clientIp, bool isAgent = false)
    {
        var (max, keys) = PostConfig(sub, clientIp, isAgent);
        return AnyExceedsAsync(keys, max);
    }

    public async Task RecordSuccessfulPostCreateAsync(string sub, string clientIp, bool isAgent = false)
    {
        var (_, keys) = PostConfig(sub, clientIp, isAgent);
        foreach (var key in keys)
            await _backend.RecordAsync(key, _postOpts.Value.WindowSeconds);
    }

    public Task<bool> IsReplyCreateRateLimitedAsync(string sub, string clientIp, bool isAgent = false)
    {
        var (max, keys) = ReplyConfig(sub, clientIp, isAgent);
        return AnyExceedsAsync(keys, max);
    }

    public async Task RecordSuccessfulReplyCreateAsync(string sub, string clientIp, bool isAgent = false)
    {
        var (_, keys) = ReplyConfig(sub, clientIp, isAgent);
        foreach (var key in keys)
            await _backend.RecordAsync(key, _postOpts.Value.WindowSeconds);
    }

    private (int max, IEnumerable<string> keys) PostConfig(string sub, string clientIp, bool isAgent)
    {
        if (isAgent)
        {
            var max = _postOpts.Value.MaxPosts * _rateLimitOpts.Value.AgentPostMultiplier;
            return (max, [$"agent:post:{sub}"]);
        }
        return (_postOpts.Value.MaxPosts, [$"post:{sub}", $"post:ip:{clientIp}"]);
    }

    private (int max, IEnumerable<string> keys) ReplyConfig(string sub, string clientIp, bool isAgent)
    {
        if (isAgent)
        {
            var max = _postOpts.Value.MaxReplies * _rateLimitOpts.Value.AgentPostMultiplier;
            return (max, [$"agent:reply:{sub}"]);
        }
        return (_postOpts.Value.MaxReplies, [$"reply:{sub}", $"reply:ip:{clientIp}"]);
    }

    private async Task<bool> AnyExceedsAsync(IEnumerable<string> keys, int max)
    {
        foreach (var key in keys)
        {
            if (await _backend.WouldExceedAsync(key, max, _postOpts.Value.WindowSeconds))
                return true;
        }
        return false;
    }
}
