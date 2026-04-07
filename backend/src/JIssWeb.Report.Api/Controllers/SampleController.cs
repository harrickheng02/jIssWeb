using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Report.Api.Controllers;

[ApiController]
[Route("api/sample")]
public class SampleController : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public ApiResult<string> Me() => ApiResult<string>.Ok($"report:{User.GetUserId()}");
}
