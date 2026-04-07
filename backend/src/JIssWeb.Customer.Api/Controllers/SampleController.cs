using JIssWeb.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Customer.Api.Controllers;

[ApiController]
[Route("api/sample")]
public class SampleController : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public ApiResult<string> Me() => ApiResult<string>.Ok("customer-authorized");
}
