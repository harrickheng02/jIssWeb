using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JIssWeb.Model.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class BlockForumMutedAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.User.Identity?.IsAuthenticated ?? true)
        {
            await next();
            return;
        }

        string sub;
        try
        {
            sub = context.HttpContext.User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            await next();
            return;
        }

        var client = context.HttpContext.RequestServices.GetRequiredService<IUserSanctionClient>();
        var status = await client.GetForumSanctionStatusAsync(sub, context.HttpContext.RequestAborted);
        if (!status.IsMuted)
        {
            await next();
            return;
        }

        context.Result = new ObjectResult(new ApiResult<ForumMutedPayload>
        {
            Success = false,
            Message = "您已被禁言",
            Code = "FORUM_MUTED",
            Data = new ForumMutedPayload { MutedUntilUtc = status.MutedUntilUtc },
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}

public sealed class ForumMutedPayload
{
    public DateTime? MutedUntilUtc { get; set; }
}
