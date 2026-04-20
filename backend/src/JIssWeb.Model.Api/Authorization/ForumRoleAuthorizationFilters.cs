using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JIssWeb.Model.Api.Authorization;

public sealed class RequireForumModeratorAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var role = user.GetForumPrincipalRole();
        if (role != ForumPrincipalRole.Moderator && role != ForumPrincipalRole.Admin)
            context.Result = new ObjectResult(ApiResult.Fail("无权访问", "FORBIDDEN")) { StatusCode = StatusCodes.Status403Forbidden };
        return Task.CompletedTask;
    }
}

public sealed class RequireForumAdminAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        if (user.GetForumPrincipalRole() != ForumPrincipalRole.Admin)
            context.Result = new ObjectResult(ApiResult.Fail("无权访问", "FORBIDDEN")) { StatusCode = StatusCodes.Status403Forbidden };
        return Task.CompletedTask;
    }
}
