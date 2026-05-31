using JIssWeb.Common;
using JIssWeb.User.Api.Authorization;
using JIssWeb.User.Api.Models;
using JIssWeb.User.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.User.Api.Controllers;

[ApiController]
[Route("api/internal/users/{sub}/")]
[RequireInternalApiKey]
public sealed class InternalSanctionsController : ControllerBase
{
    private readonly UserSanctionService _sanctions;

    public InternalSanctionsController(UserSanctionService sanctions) => _sanctions = sanctions;

    [HttpGet("forum-sanction-status")]
    public async Task<ActionResult<ApiResult<ForumSanctionStatusDto>>> GetStatus(string sub, CancellationToken ct)
    {
        var s = (sub ?? "").Trim();
        if (s.Length == 0)
            return BadRequest(ApiResult<ForumSanctionStatusDto>.Fail("用户无效", "INVALID_USER"));

        var status = await _sanctions.GetForumSanctionStatusAsync(s, ct);
        return Ok(ApiResult<ForumSanctionStatusDto>.Ok(status));
    }

    [HttpPost("sanctions")]
    public async Task<ActionResult<ApiResult<InternalSanctionCreatedDto>>> Create(
        string sub,
        [FromBody] InternalCreateSanctionRequest body,
        CancellationToken ct)
    {
        var s = (sub ?? "").Trim();
        if (s.Length == 0)
            return BadRequest(ApiResult<InternalSanctionCreatedDto>.Fail("用户无效", "INVALID_USER"));

        if (body is null || string.IsNullOrWhiteSpace(body.Type) || string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(ApiResult<InternalSanctionCreatedDto>.Fail("参数无效", "INVALID_REQUEST"));

        var type = body.Type.Trim().ToLowerInvariant();
        if (type != UserSanctionTypes.Warning && type != UserSanctionTypes.Mute)
            return BadRequest(ApiResult<InternalSanctionCreatedDto>.Fail("处罚类型无效", "INVALID_TYPE"));

        if (string.IsNullOrWhiteSpace(body.OperatorSub))
            return BadRequest(ApiResult<InternalSanctionCreatedDto>.Fail("操作人无效", "INVALID_OPERATOR"));

        try
        {
            var record = await _sanctions.CreateAsync(
                s,
                type,
                body.Reason.Trim(),
                body.OperatorSub.Trim(),
                body.ReportId,
                body.DurationPreset ?? UserSanctionDurationPresets.Hours24,
                ct);

            return Ok(ApiResult<InternalSanctionCreatedDto>.Ok(new InternalSanctionCreatedDto
            {
                SanctionId = record.Id,
                Type = record.Type,
                ExpiresAtUtc = record.ExpiresAtUtc,
                DurationPreset = record.DurationPreset,
            }));
        }
        catch (ArgumentException)
        {
            return BadRequest(ApiResult<InternalSanctionCreatedDto>.Fail("禁言时长无效", "INVALID_DURATION_PRESET"));
        }
    }

    [HttpPost("sanctions/{sanctionId}/revoke")]
    public async Task<ActionResult<ApiResult<InternalSanctionRevokedDto>>> Revoke(
        string sub,
        string sanctionId,
        [FromBody] InternalRevokeSanctionRequest body,
        CancellationToken ct)
    {
        var s = (sub ?? "").Trim();
        var sid = (sanctionId ?? "").Trim();
        if (s.Length == 0 || sid.Length == 0)
            return BadRequest(ApiResult<InternalSanctionRevokedDto>.Fail("参数无效", "INVALID_REQUEST"));

        if (body is null || string.IsNullOrWhiteSpace(body.RevokeReason))
            return BadRequest(ApiResult<InternalSanctionRevokedDto>.Fail("解封原因必填", "INVALID_REVOKE_REASON"));

        if (string.IsNullOrWhiteSpace(body.RevokedBySub))
            return BadRequest(ApiResult<InternalSanctionRevokedDto>.Fail("操作人无效", "INVALID_OPERATOR"));

        var updated = await _sanctions.RevokeMuteAsync(s, sid, body.RevokedBySub.Trim(), body.RevokeReason.Trim(), ct);
        if (updated is null)
            return NotFound(ApiResult<InternalSanctionRevokedDto>.Fail("禁言记录不存在或已解除", "NOT_FOUND"));

        return Ok(ApiResult<InternalSanctionRevokedDto>.Ok(new InternalSanctionRevokedDto { SanctionId = updated.Id }));
    }
}

public class InternalCreateSanctionRequest
{
    public string Type { get; set; } = "";
    public string Reason { get; set; } = "";
    public string OperatorSub { get; set; } = "";
    public string? ReportId { get; set; }
    public string? DurationPreset { get; set; }
}

public class InternalRevokeSanctionRequest
{
    public string RevokedBySub { get; set; } = "";
    public string RevokeReason { get; set; } = "";
}

public class InternalSanctionCreatedDto
{
    public string SanctionId { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DurationPreset { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}

public class InternalSanctionRevokedDto
{
    public string SanctionId { get; set; } = "";
}
