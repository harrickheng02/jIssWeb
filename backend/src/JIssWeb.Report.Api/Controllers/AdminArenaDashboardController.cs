using JIssWeb.Report.Api.Authorization;
using JIssWeb.Report.Api.Models;
using JIssWeb.Report.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Report.Api.Controllers;

/// <summary>
/// 运营仪表盘只读聚合端点（#36）。
/// 鉴权：仅 moderator / admin（RequireForumModerator）；member / 匿名返回 403 / 401。
/// 只读：不写任何集合（见 ArenaDashboardService 只读纪律）。
/// </summary>
[ApiController, Route("api/report"), Authorize, RequireForumModerator]
public sealed class AdminArenaDashboardController : ControllerBase
{
    private readonly IArenaDashboardService _service;

    public AdminArenaDashboardController(IArenaDashboardService service) => _service = service;

    [HttpGet("arena-dashboard")]
    public async Task<ActionResult<ArenaDashboardViewModel>> Get(CancellationToken ct)
    {
        var vm = await _service.BuildAsync(ct);
        return Ok(vm);
    }
}
