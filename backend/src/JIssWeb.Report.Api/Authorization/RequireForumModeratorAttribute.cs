using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JIssWeb.Report.Api.Authorization;

/// <summary>
/// 与 JIssWeb.Model.Api 同名的版主/管理员鉴权属性（#36）。
/// 本服务不引用 Model.Api 程序集，因此在此自包含一份；语义与上游一致：仅 moderator / admin 放行。
/// </summary>
public sealed class RequireForumModeratorAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var role = user.GetForumPrincipalRole();
        if (role != ForumPrincipalRole.Moderator && role != ForumPrincipalRole.Admin)
            context.Result = new ObjectResult(ApiResult.Fail("无权访问", "FORBIDDEN"))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        return Task.CompletedTask;
    }
}
