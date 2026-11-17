using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Model.Api.Controllers;

/// <summary>
/// 账号轮换器管理面（V0.3）。仅版主/管理员可访问；status 与 runs 为纯只读，run 为手动触发一次。
/// 手动触发不写 <c>forum_moderation_audit</c>（轮换是系统行为而非人工审核动作），
/// 责任人通过运行记录的 <c>triggeredBySub</c> 追溯。
/// </summary>
[ApiController]
[Route("api/forum/admin/agent-rotation")]
[Authorize]
[RequireForumModerator]
public sealed class AdminAgentRotationController : ControllerBase
{
    private const int DefaultRunsLimit = 20;
    private const int MaxRunsLimit = 100;

    private readonly IAgentRotationService _rotation;

    public AdminAgentRotationController(IAgentRotationService rotation) => _rotation = rotation;

    [HttpGet("status")]
    public async Task<ActionResult<ApiResult<AgentRotationStatusDto>>> Status(CancellationToken ct)
    {
        var status = await _rotation.GetStatusAsync(ct);
        return Ok(ApiResult<AgentRotationStatusDto>.Ok(new AgentRotationStatusDto
        {
            Enabled = status.Enabled,
            ActiveCount = status.ActiveCount,
            MinActiveAgents = status.MinActiveAgents,
            MaxActiveAgents = status.MaxActiveAgents,
            MaxPerRun = status.MaxPerRun,
            Gap = status.Gap,
        }));
    }

    [HttpGet("runs")]
    public async Task<ActionResult<ApiResult<List<AgentRotationRunDto>>>> Runs(
        [FromQuery] int limit = DefaultRunsLimit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, MaxRunsLimit);
        var runs = await _rotation.ListRunsAsync(take, ct);
        return Ok(ApiResult<List<AgentRotationRunDto>>.Ok(runs.Select(ToDto).ToList()));
    }

    [HttpPost("run")]
    public async Task<ActionResult<ApiResult<AgentRotationRunDto>>> Run(CancellationToken ct)
    {
        var status = await _rotation.GetStatusAsync(ct);
        if (!status.Enabled)
            return BadRequest(ApiResult<AgentRotationRunDto>.Fail("账号轮换未启用", "ROTATION_DISABLED"));

        var runKey = $"{AgentRotationTriggers.Manual}:{DateTime.UtcNow:yyyyMMddHHmmss}";
        var run = await _rotation.EnsurePoolAsync(AgentRotationTriggers.Manual, runKey, TryGetSub(), ct);
        if (run is null)
            return Conflict(ApiResult<AgentRotationRunDto>.Fail("该触发已被处理，请稍后重试", "ROTATION_ALREADY_RUNNING"));

        return Ok(ApiResult<AgentRotationRunDto>.Ok(ToDto(run)));
    }

    private string? TryGetSub()
    {
        try { return User.GetUserId(); }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static AgentRotationRunDto ToDto(AgentRotationRunRecord record) => new()
    {
        Id = record.Id,
        RunKey = record.RunKey,
        Trigger = record.Trigger,
        GapBefore = record.GapBefore,
        CreatedCount = record.CreatedCount,
        FailedCount = record.FailedCount,
        ActiveCountAfter = record.ActiveCountAfter,
        StartedAtUtc = record.StartedAtUtc,
        CompletedAtUtc = record.CompletedAtUtc,
        TriggeredBySub = record.TriggeredBySub,
        Failures = record.Failures,
    };
}

public sealed class AgentRotationStatusDto
{
    public bool Enabled { get; set; }
    public long ActiveCount { get; set; }
    public int MinActiveAgents { get; set; }
    public int MaxActiveAgents { get; set; }
    public int MaxPerRun { get; set; }
    public long Gap { get; set; }
}

public sealed class AgentRotationRunDto
{
    public string Id { get; set; } = "";
    public string RunKey { get; set; } = "";
    public string Trigger { get; set; } = "";
    public int GapBefore { get; set; }
    public int CreatedCount { get; set; }
    public int FailedCount { get; set; }
    public int ActiveCountAfter { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? TriggeredBySub { get; set; }
    public List<string> Failures { get; set; } = new();
}
