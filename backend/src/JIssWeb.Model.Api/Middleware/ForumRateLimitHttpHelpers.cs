namespace JIssWeb.Model.Api.Middleware;

internal static class ForumRateLimitHttpHelpers
{
    internal const string AgentAccountType = "agent";

    internal static bool IsAgentAccount(HttpContext context) =>
        context.User.FindFirst("accountType")?.Value == AgentAccountType;

    internal static string GetClientIp(HttpContext context)
    {
        var fwd = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fwd))
        {
            var first = fwd.Split(',')[0].Trim();
            if (first.Length > 0)
                return first;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    internal static string? TryGetAuthenticatedSub(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return null;
        try
        {
            return context.User.FindFirst("sub")?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
