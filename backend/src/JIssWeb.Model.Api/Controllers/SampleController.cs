using JIssWeb.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/sample")]
public class SampleController : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public ApiResult<string> Me() => ApiResult<string>.Ok("model-authorized");
}
