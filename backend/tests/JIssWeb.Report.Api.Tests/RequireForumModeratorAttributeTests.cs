using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using JIssWeb.Common.Security;
using JIssWeb.Report.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// RequireForumModerator 鉴权属性单测：仅 moderator / admin 放行，member / 匿名交由上层处理。
/// 语义与 Model.Api 同名属性一致（#36 自包含复刻）。
/// </summary>
public class RequireForumModeratorAttributeTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

    private static AuthorizationFilterContext Context(ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    [Fact]
    public async Task 匿名_不设Result_交由Authorize返回401()
    {
        var attr = new RequireForumModeratorAttribute();
        var ctx = Context(new ClaimsPrincipal(new ClaimsIdentity())); // 未认证
        await attr.OnAuthorizationAsync(ctx);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task 会员_返回403()
    {
        var attr = new RequireForumModeratorAttribute();
        var user = Principal(new Claim(ForumRoleClaim.Name, ForumRoleClaim.Member));
        var ctx = Context(user);
        await attr.OnAuthorizationAsync(ctx);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task 版主_放行()
    {
        var attr = new RequireForumModeratorAttribute();
        var user = Principal(new Claim(ForumRoleClaim.Name, ForumRoleClaim.Moderator));
        var ctx = Context(user);
        await attr.OnAuthorizationAsync(ctx);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task 管理员_放行()
    {
        var attr = new RequireForumModeratorAttribute();
        var user = Principal(new Claim(ForumRoleClaim.Name, ForumRoleClaim.Admin));
        var ctx = Context(user);
        await attr.OnAuthorizationAsync(ctx);
        Assert.Null(ctx.Result);
    }
}
