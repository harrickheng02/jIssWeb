using JIssWeb.Common;

namespace JIssWeb.Frontend.Bff.Middleware;

public class BffSourceHeaderMiddleware(RequestDelegate next, ILogger<BffSourceHeaderMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "";

        var isChangingBffAuthEndpoint =
            (HttpMethods.IsPost(method) || HttpMethods.IsDelete(method)) &&
            path.StartsWith("/api/bff/auth/", StringComparison.OrdinalIgnoreCase);

        if (isChangingBffAuthEndpoint)
        {
            var source = context.Request.Headers["X-BFF-Source"].ToString();
            if (!string.Equals(source, "web", StringComparison.Ordinal))
            {
                logger.LogWarning("BFF: missing or invalid X-BFF-Source header on {Method} {Path}", method, path);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(
                    ApiResult.Fail("缺少 X-BFF-Source 请求头", "MISSING_BFF_SOURCE"));
                return;
            }
        }

        await next(context);
    }
}
