using JIssWeb.Common;
using JIssWeb.Report.Api.Models;
using JIssWeb.Report.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Report.Api.Controllers;

/// <summary>
/// 图灵名人堂只读聚合端点（#37）。
/// 鉴权：匿名可访问（无 Authorize / RequireForumModerator），对应验收「公开页面」。
/// 只读：不写任何集合（见 ArenaHallOfTuringService 只读纪律）。
/// </summary>
[ApiController, Route("api/report")]
public sealed class ArenaHallOfTuringController : ControllerBase
{
    private readonly IHallOfTuringService _service;

    public ArenaHallOfTuringController(IHallOfTuringService service) => _service = service;

    [HttpGet("arena/hall-of-turing")]
    public async Task<ActionResult<HallOfTuringViewModel>> Hall(CancellationToken ct)
        => Ok(await _service.HallAsync(ct));

    [HttpGet("arena/profile/{personaId}")]
    public async Task<ActionResult<ArenaProfileViewModel>> Profile(string personaId, CancellationToken ct)
    {
        var vm = await _service.ProfileAsync(personaId, ct);
        if (vm is null)
            return NotFound(ApiResult.Fail("未找到该名人堂档案", "NOT_FOUND"));
        return Ok(vm);
    }

    [HttpGet("arena/aiqi-trend")]
    public async Task<ActionResult<List<AiqiTrendPoint>>> AiqiTrend([FromQuery] int limitWeeks = 12, CancellationToken ct = default)
        => Ok(await _service.AiqiTrendAsync(limitWeeks, ct));
}
