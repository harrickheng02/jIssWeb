using JIssWeb.User.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace JIssWeb.User.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireInternalApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-JIssWeb-Internal-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var opts = context.HttpContext.RequestServices.GetRequiredService<IOptions<InternalServiceOptions>>().Value;
        var configured = opts.ApiKey?.Trim() ?? "";
        if (configured.Length == 0)
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "内网密钥未配置", code = "UNAUTHORIZED" });
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !string.Equals(provided.ToString().Trim(), configured, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "未授权", code = "UNAUTHORIZED" });
        }
    }
}
