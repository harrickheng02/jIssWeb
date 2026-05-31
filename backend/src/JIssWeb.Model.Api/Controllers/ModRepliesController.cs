using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/mod/replies")]
public sealed class ModRepliesController : ControllerBase
{
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReportRecord> _reports;
    private readonly IMongoCollection<ForumModerationAuditRecord> _audit;
    private readonly ForumModerationAccessService _access;
    private readonly ForumModerationDeleteService _delete;
    private readonly ForumAuthorDisplayResolver _displayNames;
    private readonly IOptions<ForumBoardsOptions> _boards;
    private readonly ILogger<ModRepliesController> _logger;

    public ModRepliesController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumModerationAccessService access,
        ForumModerationDeleteService delete,
        ForumAuthorDisplayResolver displayNames,
        IOptions<ForumBoardsOptions> boards,
        ILogger<ModRepliesController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _access = access;
        _delete = delete;
        _displayNames = displayNames;
        _boards = boards;
        _logger = logger;
    }

    [HttpGet("{replyId}")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<ModReplySnapshotDto>>> GetReply(string replyId, CancellationToken ct)
    {
        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<ModReplySnapshotDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        var rid = (replyId ?? "").Trim();
        if (rid.Length == 0)
            return BadRequest(ApiResult<ModReplySnapshotDto>.Fail("回复无效", "INVALID_REPLY_ID"));

        var reply = await _replies.Find(x => x.Id == rid).FirstOrDefaultAsync(ct);
        if (reply is null)
            return NotFound(ApiResult<ModReplySnapshotDto>.Fail("回复不存在或已删除", "NOT_FOUND"));

        var post = await _posts.Find(x => x.Id == reply.PostId).FirstOrDefaultAsync(ct);
        if (post is null)
            return NotFound(ApiResult<ModReplySnapshotDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator && !_access.CanModeratePostAsModerator(User, sub, post))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<ModReplySnapshotDto>.Fail("无权查看该回复", "FORBIDDEN"));

        var names = await _displayNames.ResolveAsync(new[] { reply.AuthorSubId }, ct);
        var authorDisplayName = names.TryGetValue(reply.AuthorSubId, out var dn)
            ? dn
            : ForumDisplayName.ForSub(reply.AuthorSubId);

        return Ok(ApiResult<ModReplySnapshotDto>.Ok(new ModReplySnapshotDto
        {
            Id = reply.Id,
            PostId = reply.PostId,
            AuthorId = reply.AuthorSubId,
            AuthorDisplayName = authorDisplayName,
            Body = reply.Body,
            State = reply.State,
            CreatedAtUtc = reply.CreatedAtUtc,
        }));
    }

    [HttpDelete("{replyId}")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<string>>> DeleteReply(
        string replyId,
        [FromBody] ModReportContextRequest? body,
        CancellationToken ct)
    {
        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<string>.Fail("未授权", "UNAUTHORIZED"));
        }

        var reply = await _replies.Find(x => x.Id == replyId).FirstOrDefaultAsync(ct);
        if (reply is null)
            return NotFound(ApiResult<string>.Fail("回复不存在或已删除", "NOT_FOUND"));

        var post = await _posts.Find(x => x.Id == reply.PostId).FirstOrDefaultAsync(ct);
        if (post is null)
            return NotFound(ApiResult<string>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModeratePostAsModerator(User, sub, post))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权操作该回复", "FORBIDDEN"));
        }

        if (body?.ReportId is { Length: > 0 } rid)
        {
            var report = await _reports.Find(x => x.Id == rid.Trim()).FirstOrDefaultAsync(ct);
            if (report is null)
                return BadRequest(ApiResult<string>.Fail("举报不存在", "INVALID_REPORT_ID"));
            if (role == ForumPrincipalRole.Moderator
                && !_access.CanModerateBoardIdAsModerator(User, sub, report.BoardId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权处理该举报", "FORBIDDEN"));
            }
            if (string.Equals(report.TargetType, "reply", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(report.TargetId, replyId, StringComparison.Ordinal))
            {
                return BadRequest(ApiResult<string>.Fail("举报与回复不匹配", "INVALID_REPORT_ID"));
            }
        }

        var outcome = await _delete.TryDeleteReplyAsync(User, sub, replyId, ct);
        if (outcome == ModerationDeletionOutcome.NotFound)
            return NotFound(ApiResult<string>.Fail("回复不存在或已删除", "NOT_FOUND"));
        if (outcome == ModerationDeletionOutcome.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权操作该回复", "FORBIDDEN"));

        var now = DateTime.UtcNow;
        var meta = new Dictionary<string, object>
        {
            ["postId"] = reply.PostId,
            ["board"] = post.Board,
        };
        var boardIdResolved = ForumBoardIdLookup.ResolveBoardIdFromTitle(_boards.Value, post.Board);
        if (!string.IsNullOrEmpty(boardIdResolved))
            meta["boardId"] = boardIdResolved;
        if (body?.ReportId is { Length: > 0 } reportId)
        {
            meta["reportId"] = reportId.Trim();
            var reason = body.Reason?.Trim();
            if (!string.IsNullOrEmpty(reason))
                meta["reason"] = reason;
        }

        try
        {
            await _audit.InsertOneAsync(new ForumModerationAuditRecord
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                TargetType = "reply",
                TargetId = replyId,
                Action = "reply.modDelete",
                OperatorSub = sub,
                OccurredAtUtc = now,
                Metadata = meta,
            }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit failed after moderator delete reply {ReplyId}; content already deleted", replyId);
        }

        return Ok(ApiResult<string>.Ok("ok"));
    }
}

public class ModReplySnapshotDto
{
    public string Id { get; set; } = "";
    public string PostId { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string Body { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
