using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/mod/reports")]
public sealed class ModReportsController : ControllerBase
{
    private readonly IMongoCollection<ForumReportRecord> _reports;
    private readonly IMongoCollection<ForumReportEvidenceSnapshotRecord> _evidenceSnapshots;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IMongoCollection<InAppNotificationRecord> _notifications;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumModerationAuditRecord> _audit;
    private readonly ForumModerationAccessService _access;
    private readonly ForumAuthorDisplayResolver _displayNames;
    private readonly ForumReportTargetResolver _targetResolver;
    private readonly ILogger<ModReportsController> _logger;

    public ModReportsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumModerationAccessService access,
        ForumAuthorDisplayResolver displayNames,
        ForumReportTargetResolver targetResolver,
        ILogger<ModReportsController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        _evidenceSnapshots = db.GetCollection<ForumReportEvidenceSnapshotRecord>(ForumMongoSetup.ReportEvidenceSnapshotsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _notifications = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _access = access;
        _displayNames = displayNames;
        _targetResolver = targetResolver;
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<PagedForumReportsDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedForumReportsDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<PagedForumReportsDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        FilterDefinitionBuilder<ForumReportRecord> fb = Builders<ForumReportRecord>.Filter;
        var filter = fb.Empty;

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            var scope = _access.GetModeratorBoardIdScope(User, sub);
            if (scope is null)
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<PagedForumReportsDto>.Fail("无权访问", "FORBIDDEN"));
            filter &= fb.In(x => x.BoardId, scope);
        }

        var st = (status ?? "").Trim().ToLowerInvariant();
        if (st.Length > 0)
        {
            var bucket = NormalizeListStatusBucket(st);
            if (bucket is null)
                return BadRequest(ApiResult<PagedForumReportsDto>.Fail("状态无效", "INVALID_STATUS"));
            filter &= BuildStoredStatusFilter(fb, bucket);
        }

        var total = await _reports.CountDocumentsAsync(filter, cancellationToken: ct);
        var rows = await _reports.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var targetSubByReportId = new Dictionary<string, string?>(rows.Count);
        foreach (var r in rows)
            targetSubByReportId[r.Id] = await _targetResolver.ResolveTargetAuthorSubAsync(r, ct);

        var subsToResolve = rows.Select(r => r.ReporterSub)
            .Concat(targetSubByReportId.Values.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!))
            .Distinct(StringComparer.Ordinal);
        var allNames = await _displayNames.ResolveAsync(subsToResolve, ct);

        var items = new List<ForumReportListItemDto>(rows.Count);
        foreach (var r in rows)
        {
            var targetAuthorSub = targetSubByReportId[r.Id];
            items.Add(ToListItemDto(r, allNames, targetAuthorSub));
        }

        return Ok(ApiResult<PagedForumReportsDto>.Ok(new PagedForumReportsDto
        {
            Items = items,
            TotalCount = (int)total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    /// <summary>Sets report workflow status (<c>pending</c> | <c>rejected</c> | <c>resolved</c>) at any time.</summary>
    [HttpPatch("{reportId}")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<ForumReportListItemDto>>> PatchStatus(
        string reportId,
        [FromBody] PatchForumReportRequest body,
        CancellationToken ct = default)
    {
        var rid = (reportId ?? "").Trim();
        if (rid.Length == 0)
            return BadRequest(ApiResult<ForumReportListItemDto>.Fail("举报无效", "INVALID_REPORT_ID"));

        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<ForumReportListItemDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Status))
            return BadRequest(ApiResult<ForumReportListItemDto>.Fail("状态无效", "INVALID_STATUS"));

        if (!TryMapPatchStatus(body.Status, out var storedStatus))
            return BadRequest(ApiResult<ForumReportListItemDto>.Fail("状态无效", "INVALID_STATUS"));

        var report = await _reports.Find(x => x.Id == rid).FirstOrDefaultAsync(ct);
        if (report is null)
            return NotFound(ApiResult<ForumReportListItemDto>.Fail("举报不存在", "NOT_FOUND"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModerateBoardIdAsModerator(User, sub, report.BoardId))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<ForumReportListItemDto>.Fail("无权处理该举报", "FORBIDDEN"));
        }

        var priorCanonical = CanonicalStatus(report.Status);
        var now = DateTime.UtcNow;
        UpdateDefinition<ForumReportRecord> update;
        if (string.Equals(storedStatus, ForumReportStatuses.Pending, StringComparison.Ordinal))
        {
            update = Builders<ForumReportRecord>.Update
                .Set(x => x.Status, ForumReportStatuses.Pending)
                .Set(x => x.ResolutionCode, (string?)null)
                .Set(x => x.HandledBySub, (string?)null)
                .Set(x => x.HandledAtUtc, (DateTime?)null)
                .Set(x => x.AcknowledgedAtUtc, (DateTime?)null)
                .Set(x => x.AcknowledgedBySub, (string?)null)
                .Set(x => x.UpdatedAtUtc, now);
        }
        else
        {
            update = Builders<ForumReportRecord>.Update
                .Set(x => x.Status, storedStatus)
                .Set(x => x.ResolutionCode, (string?)null)
                .Set(x => x.HandledBySub, sub)
                .Set(x => x.HandledAtUtc, now)
                .Set(x => x.UpdatedAtUtc, now);
        }

        var upd = await _reports.UpdateOneAsync(x => x.Id == rid, update, cancellationToken: ct);
        if (upd.MatchedCount != 1)
            return NotFound(ApiResult<ForumReportListItemDto>.Fail("举报不存在", "NOT_FOUND"));

        var updated = await _reports.Find(x => x.Id == rid).FirstOrDefaultAsync(ct);
        if (updated is null)
            return NotFound(ApiResult<ForumReportListItemDto>.Fail("举报不存在", "NOT_FOUND"));

        // Write a ReportResolved notification when the report is closed (resolved or rejected).
        if (storedStatus == ForumReportStatuses.Resolved || storedStatus == ForumReportStatuses.Rejected)
        {
            await TryWriteReportNotificationAsync(report, InAppNotificationTypes.ReportResolved, now, ct);
            if (updated.HandledAtUtc.HasValue
                && !string.Equals(priorCanonical, storedStatus, StringComparison.Ordinal))
            {
                await ForumReportModerationAuditWriter.TryWriteStatusCloseAsync(
                    _audit, _logger, updated, sub, storedStatus, updated.HandledAtUtc.Value, ct);
                await ForumReportEvidenceSnapshotWriter.TryWriteOnCloseAsync(
                    _evidenceSnapshots, _posts, _replies, _logger, updated, ct);
            }
        }

        var targetAuthorSub = await _targetResolver.ResolveTargetAuthorSubAsync(updated, ct);
        var patchNames = await _displayNames.ResolveAsync(
            new[] { updated.ReporterSub }
                .Concat(string.IsNullOrWhiteSpace(targetAuthorSub) ? Array.Empty<string>() : new[] { targetAuthorSub! }),
            ct);
        return Ok(ApiResult<ForumReportListItemDto>.Ok(ToListItemDto(updated, patchNames, targetAuthorSub)));
    }

    [HttpGet("{reportId}/evidence")]
    [Authorize]
    [RequireForumModerator]
    public async Task<IActionResult> ExportEvidence(string reportId, CancellationToken ct = default)
    {
        var rid = (reportId ?? "").Trim();
        if (rid.Length == 0)
            return BadRequest(ApiResult<object>.Fail("举报无效", "INVALID_REPORT_ID"));

        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<object>.Fail("未授权", "UNAUTHORIZED"));
        }

        var report = await _reports.Find(x => x.Id == rid).FirstOrDefaultAsync(ct);
        var snapshot = await ForumReportEvidenceExporter.FindSnapshotAsync(_evidenceSnapshots, rid, report?.HandledAtUtc, ct);

        if (report is null && snapshot is null)
        {
            var hadTrace = await ForumReportEvidenceExporter.HasHistoricalEvidenceTraceAsync(_audit, rid, ct);
            return NotFound(ApiResult<object>.Fail(
                hadTrace ? "证据已过期或不存在" : "举报不存在",
                hadTrace ? "EVIDENCE_EXPIRED" : "REPORT_NOT_FOUND"));
        }

        var boardId = report?.BoardId ?? snapshot?.BoardId ?? "";
        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (string.IsNullOrWhiteSpace(boardId)
                || !_access.CanModerateBoardIdAsModerator(User, sub, boardId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<object>.Fail("无权导出该举报证据", "FORBIDDEN"));
            }
        }

        if (report is not null && CanonicalStatus(report.Status) == ForumReportStatuses.Pending)
            return BadRequest(ApiResult<object>.Fail("仅已结案举报可导出证据", "REPORT_NOT_CLOSED"));

        if (report is not null
            && CanonicalStatus(report.Status) != ForumReportStatuses.Pending
            && snapshot is null
            && report.HandledAtUtc.HasValue)
        {
            await ForumReportEvidenceSnapshotWriter.TryWriteOnCloseAsync(
                _evidenceSnapshots, _posts, _replies, _logger, report, ct);
            snapshot = await ForumReportEvidenceExporter.FindSnapshotAsync(
                _evidenceSnapshots, rid, report.HandledAtUtc, ct);
        }

        var zip = await ForumReportEvidenceExporter.TryBuildZipAsync(
            _evidenceSnapshots, _audit, _posts, _replies, report, snapshot, sub, ct);
        if (zip is null)
            return NotFound(ApiResult<object>.Fail("证据已过期或不存在", "EVIDENCE_EXPIRED"));

        return File(zip, "application/zip", $"report-{rid}-evidence.zip");
    }

    [HttpPost("{reportId}/acknowledge")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<ForumReportListItemDto>>> Acknowledge(string reportId, CancellationToken ct = default)
    {
        var rid = (reportId ?? "").Trim();
        if (rid.Length == 0)
            return BadRequest(ApiResult<ForumReportListItemDto>.Fail("举报无效", "INVALID_REPORT_ID"));

        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<ForumReportListItemDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        var report = await _reports.Find(x => x.Id == rid).FirstOrDefaultAsync(ct);
        if (report is null)
            return NotFound(ApiResult<ForumReportListItemDto>.Fail("举报不存在", "NOT_FOUND"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModerateBoardIdAsModerator(User, sub, report.BoardId))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<ForumReportListItemDto>.Fail("无权处理该举报", "FORBIDDEN"));
        }

        if (!string.Equals(report.Status, ForumReportStatuses.Pending, StringComparison.Ordinal))
            return BadRequest(ApiResult<ForumReportListItemDto>.Fail("仅待处理举报可标记已受理", "REPORT_NOT_PENDING"));

        var now = DateTime.UtcNow;
        var update = Builders<ForumReportRecord>.Update
            .Set(x => x.AcknowledgedAtUtc, now)
            .Set(x => x.AcknowledgedBySub, sub)
            .Set(x => x.UpdatedAtUtc, now);

        var upd = await _reports.UpdateOneAsync(x => x.Id == rid, update, cancellationToken: ct);
        if (upd.MatchedCount != 1)
            return NotFound(ApiResult<ForumReportListItemDto>.Fail("举报不存在", "NOT_FOUND"));

        await TryWriteReportNotificationAsync(report, InAppNotificationTypes.ReportAcknowledged, now, ct);
        await ForumReportModerationAuditWriter.TryWriteAcknowledgeAsync(_audit, _logger, report, sub, ct);

        var updated = await _reports.Find(x => x.Id == rid).FirstOrDefaultAsync(ct);
        if (updated is null)
            return NotFound(ApiResult<ForumReportListItemDto>.Fail("举报不存在", "NOT_FOUND"));

        var targetAuthorSub = await _targetResolver.ResolveTargetAuthorSubAsync(updated, ct);
        var names = await _displayNames.ResolveAsync(
            new[] { updated.ReporterSub }
                .Concat(string.IsNullOrWhiteSpace(targetAuthorSub) ? Array.Empty<string>() : new[] { targetAuthorSub! }),
            ct);
        return Ok(ApiResult<ForumReportListItemDto>.Ok(ToListItemDto(updated, names, targetAuthorSub)));
    }

    private async Task TryWriteReportNotificationAsync(
        ForumReportRecord report,
        string notificationType,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var postTitle = await _posts
            .Find(x => x.Id == report.PostId)
            .Project(x => x.Title)
            .FirstOrDefaultAsync(ct) ?? "";

        var notification = new InAppNotificationRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            RecipientSubId = report.ReporterSub,
            Type = notificationType,
            PostId = report.PostId,
            ReportId = report.Id,
            ActorSubId = "",
            PostTitle = postTitle,
            CreatedAtUtc = nowUtc,
        };
        try
        {
            await _notifications.InsertOneAsync(notification, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Idempotent: notification already exists for this (ReportId, Type) — silently skip.
        }
    }

    /// <summary>Accepted PATCH body synonyms: pending, rejected, resolved, dismissed→rejected, acknowledged→resolved.</summary>
    private static bool TryMapPatchStatus(string raw, out string stored)
    {
        stored = "";
        var s = raw.Trim().ToLowerInvariant();
        switch (s)
        {
            case ForumReportStatuses.Pending:
                stored = ForumReportStatuses.Pending;
                return true;
            case ForumReportStatuses.Rejected:
            case ForumReportStatuses.Dismissed:
                stored = ForumReportStatuses.Rejected;
                return true;
            case ForumReportStatuses.Resolved:
            case ForumReportStatuses.Acknowledged:
                stored = ForumReportStatuses.Resolved;
                return true;
            default:
                return false;
        }
    }

    /// <returns>Canonical bucket token: pending, rejected, or resolved.</returns>
    private static string? NormalizeListStatusBucket(string st)
    {
        return st switch
        {
            ForumReportStatuses.Pending => ForumReportStatuses.Pending,
            ForumReportStatuses.Rejected or ForumReportStatuses.Dismissed => ForumReportStatuses.Rejected,
            ForumReportStatuses.Resolved or ForumReportStatuses.Acknowledged => ForumReportStatuses.Resolved,
            _ => null,
        };
    }

    private static FilterDefinition<ForumReportRecord> BuildStoredStatusFilter(
        FilterDefinitionBuilder<ForumReportRecord> fb,
        string bucket)
    {
        if (bucket == ForumReportStatuses.Pending)
            return fb.Eq(x => x.Status, ForumReportStatuses.Pending);
        if (bucket == ForumReportStatuses.Rejected)
            return fb.In(x => x.Status, new List<string> { ForumReportStatuses.Rejected, ForumReportStatuses.Dismissed });
        return fb.In(x => x.Status, new List<string> { ForumReportStatuses.Resolved, ForumReportStatuses.Acknowledged });
    }

    private static string CanonicalStatus(string? stored)
    {
        var s = (stored ?? "").Trim().ToLowerInvariant();
        if (s == ForumReportStatuses.Dismissed) return ForumReportStatuses.Rejected;
        if (s == ForumReportStatuses.Acknowledged) return ForumReportStatuses.Resolved;
        return s;
    }

    private static ForumReportListItemDto ToListItemDto(
        ForumReportRecord r,
        IReadOnlyDictionary<string, string> displayNames,
        string? targetAuthorSub)
    {
        var dn = displayNames.TryGetValue(r.ReporterSub, out var n) ? n : ForumDisplayName.ForSub(r.ReporterSub);
        string? targetAuthorDisplayName = null;
        if (!string.IsNullOrWhiteSpace(targetAuthorSub))
        {
            targetAuthorDisplayName = displayNames.TryGetValue(targetAuthorSub, out var tn)
                ? tn
                : ForumDisplayName.ForSub(targetAuthorSub);
        }

        return new ForumReportListItemDto
        {
            Id = r.Id,
            ReporterSub = r.ReporterSub,
            ReporterDisplayName = dn,
            TargetType = r.TargetType,
            TargetId = r.TargetId,
            PostId = r.PostId,
            BoardId = r.BoardId,
            BoardTitle = r.BoardTitle,
            Reason = r.Reason,
            Status = CanonicalStatus(r.Status),
            CreatedAtUtc = r.CreatedAtUtc,
            UpdatedAtUtc = r.UpdatedAtUtc,
            HandledBySub = r.HandledBySub,
            HandledAtUtc = r.HandledAtUtc,
            AcknowledgedAtUtc = r.AcknowledgedAtUtc,
            AcknowledgedBySub = r.AcknowledgedBySub,
            TargetAuthorSub = targetAuthorSub,
            TargetAuthorDisplayName = targetAuthorDisplayName,
        };
    }
}

public class PagedForumReportsDto
{
    public List<ForumReportListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ForumReportListItemDto
{
    public string Id { get; set; } = "";
    public string ReporterSub { get; set; } = "";
    public string ReporterDisplayName { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string PostId { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string BoardTitle { get; set; } = "";
    public string? Reason { get; set; }
    /// <summary>Canonical status: pending, rejected, or resolved.</summary>
    public string Status { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? HandledBySub { get; set; }
    public DateTime? HandledAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBySub { get; set; }
    public string? TargetAuthorSub { get; set; }
    public string? TargetAuthorDisplayName { get; set; }
}

public class PatchForumReportRequest
{
    public string? Status { get; set; }
}
