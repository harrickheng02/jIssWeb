using JIssWeb.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.User.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public ApiResult<string> Get() => ApiResult<string>.Ok("user");
}
