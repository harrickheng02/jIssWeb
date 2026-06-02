using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/mod/users/{sub}/sanctions")]
public sealed class ModUserSanctionsController : ControllerBase
{
    private readonly IMongoCollection<ForumReportRecord> _reports;
    private readonly IMongoCollection<ForumModerationAuditRecord> _audit;
    private readonly IMongoCollection<InAppNotificationRecord> _notifications;
    private readonly ForumModerationAccessService _access;
    private readonly ForumReportTargetResolver _targetResolver;
    private readonly IUserSanctionClient _sanctions;

    public ModUserSanctionsController(
        IMongoClient mongoClient,
        Microsoft.Extensions.Options.IOptions<JIssWeb.Common.Options.MongoSettings> mongoOptions,
        ForumModerationAccessService access,
        ForumReportTargetResolver targetResolver,
        IUserSanctionClient sanctions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _notifications = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        _access = access;
        _targetResolver = targetResolver;
        _sanctions = sanctions;
    }

    [HttpPost("~/api/mod/reports/{reportId}/sanctions")]
    [Authorize]
    [RequireForumModerator]
    public Task<ActionResult<ApiResult<ModUserSanctionResultDto>>> CreateFromReport(
        string reportId,
        [FromBody] ModReportSanctionRequest body,
        CancellationToken ct)
    {
        var rid = (reportId ?? "").Trim();
        if (rid.Length == 0)
            return Task.FromResult<ActionResult<ApiResult<ModUserSanctionResultDto>>>(
                BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("举报无效", "INVALID_REPORT_ID")));

        if (body is null)
            return Task.FromResult<ActionResult<ApiResult<ModUserSanctionResultDto>>>(
                BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("请求无效", "INVALID_BODY")));

        var wrapped = new ModUserSanctionRequest
        {
            Type = body.Type,
            Reason = body.Reason,
            ReportId = rid,
            DurationPreset = body.DurationPreset,
        };

        return CreateFromReportInternal(null, wrapped, ct);
    }

    [HttpPost]
    [Authorize]
    [RequireForumModerator]
    public Task<ActionResult<ApiResult<ModUserSanctionResultDto>>> Create(
        string sub,
        [FromBody] ModUserSanctionRequest body,
        CancellationToken ct)
    {
        var targetSub = (sub ?? "").Trim();
        if (targetSub.Length == 0)
            return Task.FromResult<ActionResult<ApiResult<ModUserSanctionResultDto>>>(
                BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("用户无效", "INVALID_USER")));

        return CreateFromReportInternal(targetSub, body, ct);
    }

    [HttpPost("{sanctionId}/revoke")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<object>>> Revoke(
        string sub,
        string sanctionId,
        [FromBody] ModRevokeSanctionRequest body,
        CancellationToken ct)
    {
        var targetSub = (sub ?? "").Trim();
        var sid = (sanctionId ?? "").Trim();
        if (targetSub.Length == 0 || sid.Length == 0)
            return BadRequest(ApiResult<object>.Fail("参数无效", "INVALID_REQUEST"));

        string operatorSub;
        try
        {
            operatorSub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<object>.Fail("未授权", "UNAUTHORIZED"));
        }

        if (body is null || string.IsNullOrWhiteSpace(body.RevokeReason))
            return BadRequest(ApiResult<object>.Fail("解封原因必填", "INVALID_REVOKE_REASON"));

        var ok = await _sanctions.RevokeMuteAsync(targetSub, sid, operatorSub, body.RevokeReason.Trim(), ct);
        if (!ok)
            return NotFound(ApiResult<object>.Fail("禁言记录不存在或已解除", "NOT_FOUND"));

        var auditMeta = await BuildUnmuteAuditMetadataAsync(targetSub, sid, body.RevokeReason.Trim(), body.ReportId, ct);

        await _audit.InsertOneAsync(new ForumModerationAuditRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TargetType = "user",
            TargetId = targetSub,
            Action = "user.unmute",
            OperatorSub = operatorSub,
            OccurredAtUtc = DateTime.UtcNow,
            Metadata = auditMeta,
        }, cancellationToken: ct);

        return Ok(ApiResult<object>.Ok(new { sanctionId = sid }));
    }

    private async Task<ActionResult<ApiResult<ModUserSanctionResultDto>>> CreateFromReportInternal(
        string? expectedTargetSub,
        ModUserSanctionRequest body,
        CancellationToken ct)
    {
        string operatorSub;
        try
        {
            operatorSub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<ModUserSanctionResultDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("原因必填", "INVALID_REASON"));

        if (string.IsNullOrWhiteSpace(body.ReportId))
            return BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("举报上下文必填", "INVALID_REPORT_ID"));

        var type = body.Type.Trim().ToLowerInvariant();
        if (type != "warning" && type != "mute")
            return BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("处罚类型无效", "INVALID_TYPE"));

        var report = await _reports.Find(x => x.Id == body.ReportId.Trim()).FirstOrDefaultAsync(ct);
        if (report is null)
            return BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("举报不存在", "INVALID_REPORT_ID"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator
            && !_access.CanModerateBoardIdAsModerator(User, operatorSub, report.BoardId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<ModUserSanctionResultDto>.Fail("无权处理该举报", "FORBIDDEN"));
        }

        var authorSub = await _targetResolver.ResolveTargetAuthorSubAsync(report, ct);
        if (authorSub is null)
            return BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("被举报内容不存在或已彻底删除", "INVALID_REPORT_TARGET"));

        if (expectedTargetSub is not null && !string.Equals(authorSub, expectedTargetSub, StringComparison.Ordinal))
            return BadRequest(ApiResult<ModUserSanctionResultDto>.Fail("处罚对象与举报目标不一致", "INVALID_TARGET_USER"));

        var targetSub = authorSub;
        var preset = string.IsNullOrWhiteSpace(body.DurationPreset) ? "24h" : body.DurationPreset.Trim();
        var created = await _sanctions.CreateSanctionAsync(
            targetSub,
            type,
            body.Reason.Trim(),
            operatorSub,
            report.Id,
            type == "mute" ? preset : null,
            ct);

        if (created is null)
            return StatusCode(StatusCodes.Status502BadGateway, ApiResult<ModUserSanctionResultDto>.Fail("处罚服务不可用", "SANCTION_SERVICE_ERROR"));

        var auditAction = type == "warning" ? "user.warn" : "user.mute";
        var meta = new Dictionary<string, object>
        {
            ["reportId"] = report.Id,
            ["postId"] = report.PostId,
            ["boardId"] = report.BoardId,
            ["reason"] = body.Reason.Trim(),
            ["sanctionId"] = created.SanctionId,
        };
        if (type == "mute")
        {
            meta["durationPreset"] = preset;
            if (created.ExpiresAtUtc.HasValue)
                meta["expiresAtUtc"] = created.ExpiresAtUtc.Value;
        }

        await _audit.InsertOneAsync(new ForumModerationAuditRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TargetType = "user",
            TargetId = targetSub,
            Action = auditAction,
            OperatorSub = operatorSub,
            OccurredAtUtc = DateTime.UtcNow,
            Metadata = meta,
        }, cancellationToken: ct);

        if (type == "warning")
        {
            try
            {
                await _notifications.InsertOneAsync(new InAppNotificationRecord
                {
                    Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    RecipientSubId = targetSub,
                    Type = InAppNotificationTypes.ForumWarning,
                    PostId = report.PostId,
                    ActorSubId = "",
                    PostTitle = "",
                    CreatedAtUtc = DateTime.UtcNow,
                }, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Non-idempotent warning path; duplicate unlikely but must not fail the sanction.
            }
        }

        return Ok(ApiResult<ModUserSanctionResultDto>.Ok(new ModUserSanctionResultDto
        {
            SanctionId = created.SanctionId,
            Type = created.Type,
            DurationPreset = created.DurationPreset,
            ExpiresAtUtc = created.ExpiresAtUtc,
        }));
    }

    private async Task<Dictionary<string, object>> BuildUnmuteAuditMetadataAsync(
        string targetSub,
        string sanctionId,
        string revokeReason,
        string? reportIdFromRequest,
        CancellationToken ct)
    {
        var meta = new Dictionary<string, object>
        {
            ["sanctionId"] = sanctionId,
            ["revokeReason"] = revokeReason,
        };

        var reportId = reportIdFromRequest?.Trim();
        if (string.IsNullOrEmpty(reportId))
        {
            var fb = Builders<ForumModerationAuditRecord>.Filter;
            var priorMute = await _audit.Find(fb.And(
                    fb.Eq(x => x.TargetType, "user"),
                    fb.Eq(x => x.TargetId, targetSub),
                    fb.Eq(x => x.Action, "user.mute"),
                    fb.Eq("Metadata.sanctionId", sanctionId)))
                .SortByDescending(x => x.OccurredAtUtc)
                .FirstOrDefaultAsync(ct);
            if (priorMute?.Metadata is not null
                && priorMute.Metadata.TryGetValue("reportId", out var ridRaw)
                && ridRaw is not null)
            {
                reportId = ridRaw.ToString()?.Trim();
                CopyAuditMetadataKeys(priorMute.Metadata, meta, "postId", "boardId");
            }
        }

        if (!string.IsNullOrEmpty(reportId))
        {
            meta["reportId"] = reportId;
            var report = await _reports.Find(x => x.Id == reportId).FirstOrDefaultAsync(ct);
            if (report is not null)
            {
                meta["postId"] = report.PostId;
                meta["boardId"] = report.BoardId;
            }
        }

        return meta;
    }

    private static void CopyAuditMetadataKeys(
        Dictionary<string, object> source,
        Dictionary<string, object> target,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (source.TryGetValue(key, out var val) && val is not null)
                target[key] = val;
        }
    }
}
