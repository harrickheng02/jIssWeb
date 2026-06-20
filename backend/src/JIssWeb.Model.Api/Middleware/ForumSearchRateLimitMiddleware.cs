using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using JIssWeb.Common;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
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
    private readonly IForumRateLimitBackend _backend;
    private readonly IOptions<ForumSearchRateLimitOptions> _options;

    public ForumSearchRateLimitMiddleware(
        RequestDelegate next,
        IForumRateLimitBackend backend,
        IOptions<ForumSearchRateLimitOptions> options)
    {
        _next = next;
        _backend = backend;
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
            if (q.Length > 0 && !ForumRateLimitHttpHelpers.IsAgentAccount(context))
            {
                var opt = _options.Value;
                var ip = ForumRateLimitHttpHelpers.GetClientIp(context);
                if (!await _backend.TryConsumeAsync($"search:ip:{ip}", opt.MaxRequests, opt.WindowSeconds))
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
