using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using System.Globalization;
using System.Text.Json.Serialization;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/mod/audit")]
public class ModAuditController : ControllerBase
{
    private readonly IMongoCollection<ForumModerationAuditRecord> _audit;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly ForumModerationAccessService _access;
    private readonly ForumAuthorDisplayResolver _displayNames;
    private readonly IOptions<ForumModerationAuditOptions> _auditOptions;
    private readonly IOptions<ForumBoardsOptions> _boards;
    private readonly ILogger<ModAuditController> _logger;

    public ModAuditController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumModerationAccessService access,
        ForumAuthorDisplayResolver displayNames,
        IOptions<ForumModerationAuditOptions> auditOptions,
        IOptions<ForumBoardsOptions> boards,
        ILogger<ModAuditController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _access = access;
        _displayNames = displayNames;
        _auditOptions = auditOptions;
        _boards = boards;
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<PagedAuditDto>>> List(
        [FromQuery] string targetType,
        [FromQuery] string targetId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string[]? action = null,
        [FromQuery] string? fromUtc = null,
        [FromQuery] string? toUtc = null,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedAuditDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        if (!string.Equals(targetType?.Trim(), "post", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResult<PagedAuditDto>.Fail("目标类型无效", "INVALID_TARGET_TYPE"));

        if (string.IsNullOrWhiteSpace(targetId))
            return BadRequest(ApiResult<PagedAuditDto>.Fail("目标无效", "INVALID_TARGET_ID"));

        if (!ModerationAuditActions.TryParseQueryActions(action, out var actions, out var invalidAction))
            return BadRequest(ApiResult<PagedAuditDto>.Fail($"操作类型无效: {invalidAction}", "INVALID_ACTION"));

        DateTime? from = null;
        DateTime? to = null;
        if (!TryParseUtcQuery(fromUtc, out from, out var fromErr))
            return BadRequest(ApiResult<PagedAuditDto>.Fail(fromErr ?? "时间无效", "INVALID_TIME"));
        if (!TryParseUtcQuery(toUtc, out to, out var toErr))
            return BadRequest(ApiResult<PagedAuditDto>.Fail(toErr ?? "时间无效", "INVALID_TIME"));
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return BadRequest(ApiResult<PagedAuditDto>.Fail("时间范围无效", "INVALID_TIME_RANGE"));

        var tid = targetId.Trim();

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            string sub;
            try
            {
                sub = User.GetUserId();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(ApiResult<PagedAuditDto>.Fail("未授权", "UNAUTHORIZED"));
            }

            var post = await _posts.Find(x => x.Id == tid).FirstOrDefaultAsync(ct);
            if (post is not null)
            {
                if (!_access.CanModeratePostAsModerator(User, sub, post))
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResult<PagedAuditDto>.Fail("无权操作该帖子", "FORBIDDEN"));
            }
            else
            {
                var threadProbe = PostThreadAuditQuery.BuildThreadFilter(tid);
                var sample = await _audit.Find(threadProbe).SortByDescending(x => x.OccurredAtUtc).Limit(1).FirstOrDefaultAsync(ct);
                if (sample is null)
                    return NotFound(ApiResult<PagedAuditDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));

                if (TryGetBoardIdFromAuditMetadata(sample, out var boardIdFromAudit) && !string.IsNullOrEmpty(boardIdFromAudit))
                {
                    if (!_access.CanModerateBoardIdAsModerator(User, sub, boardIdFromAudit!))
                        return StatusCode(StatusCodes.Status403Forbidden, ApiResult<PagedAuditDto>.Fail("无权操作该帖子", "FORBIDDEN"));
                }
                else if (TryGetBoardTitleFromAuditMetadata(sample, out var boardFromAudit) && !string.IsNullOrEmpty(boardFromAudit))
                {
                    if (!_access.CanModerateBoardTitleAsModerator(User, sub, boardFromAudit!))
                        return StatusCode(StatusCodes.Status403Forbidden, ApiResult<PagedAuditDto>.Fail("无权操作该帖子", "FORBIDDEN"));
                }
                else
                    return NotFound(ApiResult<PagedAuditDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));
            }
        }

        var filter = PostThreadAuditQuery.ApplyOptionalFilters(
            PostThreadAuditQuery.BuildThreadFilter(tid),
            actions,
            from,
            to);

        var total = await _audit.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _audit.Find(filter)
            .SortByDescending(x => x.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var nameMap = await _displayNames.ResolveAsync(items.Select(i => i.OperatorSub), ct);

        return Ok(ApiResult<PagedAuditDto>.Ok(new PagedAuditDto
        {
            Items = items.Select(x =>
            {
                var display = nameMap.TryGetValue(x.OperatorSub, out var n) ? n : ForumDisplayName.ForSub(x.OperatorSub);
                return new ModerationAuditItemDto
                {
                    Id = x.Id,
                    TargetType = x.TargetType,
                    TargetId = x.TargetId,
                    Action = x.Action,
                    ActionLabel = ModerationAuditPresentation.ActionLabel(x.Action),
                    OperatorSub = x.OperatorSub,
                    OperatorDisplayName = display,
                    OccurredAtUtc = x.OccurredAtUtc,
                };
            }).ToList(),
            TotalCount = (int)total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpGet("feed")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<PagedAuditFeedDto>>> Feed(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string[]? action = null,
        [FromQuery] string? fromUtc = null,
        [FromQuery] string? toUtc = null,
        [FromQuery] string? boardId = null,
        CancellationToken ct = default)
    {
        var feedResult = await BuildFeedQueryAsync(action, fromUtc, toUtc, boardId, ct);
        if (feedResult.Error is { } err)
            return err;

        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedAuditFeedDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        var (filter, boardTitles) = feedResult.Value;
        var total = await _audit.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _audit.Find(filter)
            .SortByDescending(x => x.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var dtoItems = await MapFeedItemsAsync(items, boardTitles, ct);
        return Ok(ApiResult<PagedAuditFeedDto>.Ok(new PagedAuditFeedDto
        {
            Items = dtoItems,
            TotalCount = (int)total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpGet("export")]
    [Authorize]
    [RequireForumModerator]
    public async Task<IActionResult> Export(
        [FromQuery] string[]? action = null,
        [FromQuery] string? fromUtc = null,
        [FromQuery] string? toUtc = null,
        [FromQuery] string? boardId = null,
        CancellationToken ct = default)
    {
        var feedResult = await BuildFeedQueryAsync(action, fromUtc, toUtc, boardId, ct);
        if (feedResult.Error is { } err)
            return err;

        var maxRows = Math.Max(1, _auditOptions.Value.MaxExportRows);
        var (filter, boardTitles) = feedResult.Value;
        var total = await _audit.CountDocumentsAsync(filter, cancellationToken: ct);
        if (total > maxRows)
            return BadRequest(ApiResult<object>.Fail("导出结果过多，请缩小筛选范围", "EXPORT_TOO_LARGE"));

        var items = await _audit.Find(filter)
            .SortByDescending(x => x.OccurredAtUtc)
            .Limit(maxRows)
            .ToListAsync(ct);

        var dtoItems = await MapFeedItemsAsync(items, boardTitles, ct);
        var bytes = AuditFeedCsvBuilder.BuildUtf8WithBom(dtoItems);
        var fileName = $"moderation-audit-{DateTime.UtcNow:yyyyMMddHHmmss}Z.csv";

        await TryWriteExportAuditAsync(action, fromUtc, toUtc, boardId, dtoItems.Count, ct);

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private Task<FeedQueryBuildResult> BuildFeedQueryAsync(
        string[]? action,
        string? fromUtc,
        string? toUtc,
        string? boardId,
        CancellationToken ct)
    {
        _ = ct;
        if (!ModerationAuditActions.TryParseQueryActions(action, out var actions, out var invalidAction))
            return Task.FromResult(FeedQueryBuildResult.Fail(BadRequest(ApiResult<PagedAuditFeedDto>.Fail($"操作类型无效: {invalidAction}", "INVALID_ACTION"))));

        DateTime? from = null;
        DateTime? to = null;
        if (!TryParseUtcQuery(fromUtc, out from, out var fromErr))
            return Task.FromResult(FeedQueryBuildResult.Fail(BadRequest(ApiResult<PagedAuditFeedDto>.Fail(fromErr ?? "时间无效", "INVALID_TIME"))));
        if (!TryParseUtcQuery(toUtc, out to, out var toErr))
            return Task.FromResult(FeedQueryBuildResult.Fail(BadRequest(ApiResult<PagedAuditFeedDto>.Fail(toErr ?? "时间无效", "INVALID_TIME"))));
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return Task.FromResult(FeedQueryBuildResult.Fail(BadRequest(ApiResult<PagedAuditFeedDto>.Fail("时间范围无效", "INVALID_TIME_RANGE"))));

        var role = User.GetForumPrincipalRole();
        IReadOnlyList<string>? modScope = null;
        if (role == ForumPrincipalRole.Moderator)
        {
            string sub;
            try
            {
                sub = User.GetUserId();
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(FeedQueryBuildResult.Fail(Unauthorized(ApiResult<PagedAuditFeedDto>.Fail("未授权", "UNAUTHORIZED"))));
            }

            modScope = _access.GetModeratorBoardIdScope(User, sub);
            if (modScope is null || modScope.Count == 0)
                return Task.FromResult(FeedQueryBuildResult.Fail(StatusCode(StatusCodes.Status403Forbidden, ApiResult<PagedAuditFeedDto>.Fail("无权查看审计", "FORBIDDEN"))));

            if (!string.IsNullOrWhiteSpace(boardId)
                && !_access.CanModerateBoardIdAsModerator(User, sub, boardId.Trim()))
            {
                return Task.FromResult(FeedQueryBuildResult.Fail(StatusCode(StatusCodes.Status403Forbidden, ApiResult<PagedAuditFeedDto>.Fail("无权查看该版区", "FORBIDDEN"))));
            }
        }

        var (fromResolved, toResolved) = AuditFeedQuery.ResolveTimeWindow(
            from,
            to,
            _auditOptions.Value.DefaultFeedDays);

        var filter = AuditFeedQuery.BuildFeedFilter(role, modScope, boardId, actions, fromResolved, toResolved);
        return Task.FromResult(FeedQueryBuildResult.Ok(filter, BuildBoardIdToTitleMap()));
    }

    private async Task<List<ModerationAuditFeedItemDto>> MapFeedItemsAsync(
        List<ForumModerationAuditRecord> items,
        IReadOnlyDictionary<string, string> boardTitles,
        CancellationToken ct)
    {
        var nameMap = await _displayNames.ResolveAsync(items.Select(i => i.OperatorSub), ct);
        return items.Select(x =>
        {
            var boardIdMeta = AuditFeedMetadata.GetString(x.Metadata, "boardId");
            var boardMetaTitle = AuditFeedMetadata.GetString(x.Metadata, "board");
            var display = nameMap.TryGetValue(x.OperatorSub, out var n) ? n : ForumDisplayName.ForSub(x.OperatorSub);
            return new ModerationAuditFeedItemDto
            {
                Id = x.Id,
                TargetType = x.TargetType,
                TargetId = x.TargetId,
                ActionLabel = ModerationAuditPresentation.ActionLabel(x.Action),
                OperatorSub = x.OperatorSub,
                OperatorDisplayName = display,
                OccurredAtUtc = x.OccurredAtUtc,
                BoardId = boardIdMeta ?? "",
                BoardLabel = AuditFeedMetadata.ResolveBoardLabel(boardIdMeta, boardMetaTitle, boardTitles),
                PostId = AuditFeedMetadata.ResolvePostId(x) ?? "",
                ReportId = AuditFeedMetadata.ResolveReportId(x) ?? "",
            };
        }).ToList();
    }

    private Dictionary<string, string> BuildBoardIdToTitleMap() =>
        _boards.Value.Boards
            .Where(b => !string.IsNullOrWhiteSpace(b.Id))
            .GroupBy(b => b.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Title?.Trim() ?? g.Key, StringComparer.OrdinalIgnoreCase);

    private async Task TryWriteExportAuditAsync(
        string[]? action,
        string? fromUtc,
        string? toUtc,
        string? boardId,
        int exportedCount,
        CancellationToken ct)
    {
        try
        {
            string sub;
            try
            {
                sub = User.GetUserId();
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            var exportId = ObjectId.GenerateNewId().ToString();
            await _audit.InsertOneAsync(new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "system",
                TargetId = exportId,
                Action = "audit.export",
                OperatorSub = sub,
                OccurredAtUtc = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["exportedCount"] = exportedCount,
                    ["fromUtc"] = fromUtc ?? "",
                    ["toUtc"] = toUtc ?? "",
                    ["boardId"] = boardId ?? "",
                    ["actions"] = action is { Length: > 0 } ? string.Join(",", action) : "",
                },
            }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write audit.export after CSV export");
        }
    }

    private static bool TryParseUtcQuery(string? raw, out DateTime? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            || DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
        {
            parsed = DateTime.SpecifyKind(dt.ToUniversalTime(), DateTimeKind.Utc);
            return true;
        }

        error = "时间格式无效";
        return false;
    }

    private static bool TryGetBoardIdFromAuditMetadata(ForumModerationAuditRecord audit, out string? boardId)
    {
        boardId = null;
        if (audit.Metadata is null || !audit.Metadata.TryGetValue("boardId", out var raw) || raw is null)
            return false;

        var s = raw switch
        {
            string x => x.Trim(),
            _ => raw.ToString()?.Trim(),
        };
        if (string.IsNullOrEmpty(s))
            return false;
        boardId = s;
        return true;
    }

    private static bool TryGetBoardTitleFromAuditMetadata(ForumModerationAuditRecord audit, out string? boardTitle)
    {
        boardTitle = null;
        if (audit.Metadata is null || !audit.Metadata.TryGetValue("board", out var raw) || raw is null)
            return false;

        var s = raw switch
        {
            string x => x.Trim(),
            _ => raw.ToString()?.Trim(),
        };
        if (string.IsNullOrEmpty(s))
            return false;
        boardTitle = s;
        return true;
    }

    private sealed class FeedQueryBuildResult
    {
        public ActionResult? Error { get; init; }
        public (FilterDefinition<ForumModerationAuditRecord> Filter, IReadOnlyDictionary<string, string> BoardTitles) Value { get; init; }

        public static FeedQueryBuildResult Fail(ActionResult error) => new() { Error = error };

        public static FeedQueryBuildResult Ok(
            FilterDefinition<ForumModerationAuditRecord> filter,
            IReadOnlyDictionary<string, string> boardTitles) =>
            new() { Value = (filter, boardTitles) };
    }
}

public class PagedAuditDto
{
    public List<ModerationAuditItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ModerationAuditItemDto
{
    public string Id { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    /// <summary>Internal action code (not written to JSON responses).</summary>
    [JsonIgnore]
    public string Action { get; set; } = "";

    /// <summary>User-facing label for the action.</summary>
    public string ActionLabel { get; set; } = "";

    [JsonIgnore]
    public string OperatorSub { get; set; } = "";

    /// <summary>Resolved nickname when available.</summary>
    public string OperatorDisplayName { get; set; } = "";

    public DateTime OccurredAtUtc { get; set; }
}

public class PagedAuditFeedDto
{
    public List<ModerationAuditFeedItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ModerationAuditFeedItemDto
{
    public string Id { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string ActionLabel { get; set; } = "";
    [JsonIgnore]
    public string OperatorSub { get; set; } = "";
    public string OperatorDisplayName { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string BoardId { get; set; } = "";
    public string BoardLabel { get; set; } = "";
    public string PostId { get; set; } = "";
    public string ReportId { get; set; } = "";
}
