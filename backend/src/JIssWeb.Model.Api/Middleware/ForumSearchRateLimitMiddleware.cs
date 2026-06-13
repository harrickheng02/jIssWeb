using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using JIssWeb.Common;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Middleware;

public sealed class ForumSearchRateLimitMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RequestDelegate _next;
    private readonly InProcessSlidingWindowRateLimiter _limiter;
    private readonly IOptions<ForumSearchRateLimitOptions> _options;

    public ForumSearchRateLimitMiddleware(
        RequestDelegate next,
        InProcessSlidingWindowRateLimiter limiter,
        IOptions<ForumSearchRateLimitOptions> options)
    {
        _next = next;
        _limiter = limiter;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var req = context.Request;
        if (HttpMethods.IsGet(req.Method)
            && req.Path.Equals("/api/forum/posts", StringComparison.Ordinal)
            && req.Query.TryGetValue("q", out var qv))
        {
            var q = qv.ToString().Trim();
            if (q.Length > 0)
            {
                var opt = _options.Value;
                var ip = ForumRateLimitHttpHelpers.GetClientIp(context);
                if (!_limiter.TryConsume(ip, opt.MaxRequests, opt.WindowSeconds))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    var body = ApiResult.Fail("请求过于频繁", "RATE_LIMITED");
                    await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
                    return;
                }
            }
        }

        await _next(context);
    }
}
