using JIssWeb.Common;
using JIssWeb.Model.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Model.Api.Controllers;

/// <summary>
/// Placeholder routes for forum role contract (moderator / admin). Outside Development, the host returns 404 for this path prefix before these actions run.
/// </summary>
[ApiController]
[Route("api/forum/__debug")]
public class ForumGovernanceDebugController : ControllerBase
{
    [HttpGet("moderator")]
    [Authorize]
    [RequireForumModerator]
    public ActionResult<ApiResult<string>> ModeratorOnly() => Ok(ApiResult<string>.Ok("ok"));

    [HttpGet("admin")]
    [Authorize]
    [RequireForumAdmin]
    public ActionResult<ApiResult<string>> AdminOnly() => Ok(ApiResult<string>.Ok("ok"));
}
