using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using System.Text.Json.Serialization;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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

    public ModAuditController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumModerationAccessService access,
        ForumAuthorDisplayResolver displayNames)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _access = access;
        _displayNames = displayNames;
    }

    [HttpGet]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<PagedAuditDto>>> List(
        [FromQuery] string targetType,
        [FromQuery] string targetId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedAuditDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        if (!string.Equals(targetType?.Trim(), "post", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResult<PagedAuditDto>.Fail("目标类型无效", "INVALID_TARGET_TYPE"));

        if (string.IsNullOrWhiteSpace(targetId))
            return BadRequest(ApiResult<PagedAuditDto>.Fail("目标无效", "INVALID_TARGET_ID"));

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
                var scopeProbe = Builders<ForumModerationAuditRecord>.Filter.Eq(x => x.TargetType, "post")
                                 & Builders<ForumModerationAuditRecord>.Filter.Eq(x => x.TargetId, tid);
                var sample = await _audit.Find(scopeProbe).SortByDescending(x => x.OccurredAtUtc).Limit(1).FirstOrDefaultAsync(ct);
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

        var filter = Builders<ForumModerationAuditRecord>.Filter.Eq(x => x.TargetType, "post")
                     & Builders<ForumModerationAuditRecord>.Filter.Eq(x => x.TargetId, tid);

        var total = await _audit.CountDocumentsAsync(filter);
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

