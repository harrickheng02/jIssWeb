using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoWriteException = MongoDB.Driver.MongoWriteException;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/reports")]
public sealed class ForumReportsController : ControllerBase
{
    public const int MaxReasonLength = 500;

    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IMongoCollection<ForumReportRecord> _reports;
    private readonly IOptions<ForumBoardsOptions> _boards;
    private readonly ILogger<ForumReportsController> _logger;

    public ForumReportsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boards,
        ILogger<ForumReportsController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        _boards = boards;
        _logger = logger;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ApiResult<ForumReportCreatedDto>>> Create(
        [FromBody] CreateForumReportRequest body,
        CancellationToken ct)
    {
        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<ForumReportCreatedDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        if (body is null)
            return BadRequest(ApiResult<ForumReportCreatedDto>.Fail("请求无效", "INVALID_BODY"));

        var targetType = (body.TargetType ?? "").Trim().ToLowerInvariant();
        if (targetType is not ("post" or "reply"))
            return BadRequest(ApiResult<ForumReportCreatedDto>.Fail("目标类型无效", "INVALID_TARGET_TYPE"));

        var targetId = (body.TargetId ?? "").Trim();
        if (targetId.Length == 0)
            return BadRequest(ApiResult<ForumReportCreatedDto>.Fail("目标无效", "INVALID_TARGET_ID"));

        var reasonRaw = body.Reason?.Trim() ?? "";
        if (reasonRaw.Length > MaxReasonLength)
            return BadRequest(ApiResult<ForumReportCreatedDto>.Fail("说明过长", "REASON_TOO_LONG"));

        string? reason = reasonRaw.Length == 0 ? null : reasonRaw;

        var resolved = await TryResolveTargetAsync(targetType, targetId, ct);
        if (resolved is null)
            return NotFound(ApiResult<ForumReportCreatedDto>.Fail("帖子或回复不存在", "NOT_FOUND"));

        var (boardId, boardTitle, postId) = resolved.Value;
        var now = DateTime.UtcNow;
        var report = new ForumReportRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            ReporterSub = sub,
            TargetType = targetType,
            TargetId = targetId,
            PostId = postId,
            BoardId = boardId,
            BoardTitle = boardTitle,
            Reason = reason,
            Status = ForumReportStatuses.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        try
        {
            await _reports.InsertOneAsync(report, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            _logger.LogDebug(ex, "Duplicate pending forum report");
            return Conflict(ApiResult<ForumReportCreatedDto>.Fail(
                "待处理举报已存在", "DUPLICATE_PENDING_REPORT"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Insert forum report failed");
            throw;
        }

        return Ok(ApiResult<ForumReportCreatedDto>.Ok(new ForumReportCreatedDto { Id = report.Id }));
    }

    private async Task<(string boardId, string boardTitle, string postId)?> TryResolveTargetAsync(
        string targetType,
        string targetId,
        CancellationToken ct)
    {
        if (targetType == "post")
        {
            var post = await _posts.Find(x => x.Id == targetId).FirstOrDefaultAsync(ct);
            if (post is null) return null;
            var bid = ForumBoardIdLookup.ResolveBoardIdFromTitle(_boards.Value, post.Board);
            if (string.IsNullOrWhiteSpace(bid)) return null;
            return (bid.Trim(), (post.Board ?? "").Trim(), post.Id);
        }

        var reply = await _replies.Find(x => x.Id == targetId).FirstOrDefaultAsync(ct);
        if (reply is null) return null;
        var thread = await _posts.Find(x => x.Id == reply.PostId).FirstOrDefaultAsync(ct);
        if (thread is null) return null;
        var rb = ForumBoardIdLookup.ResolveBoardIdFromTitle(_boards.Value, thread.Board);
        if (string.IsNullOrWhiteSpace(rb)) return null;
        return (rb.Trim(), (thread.Board ?? "").Trim(), thread.Id);
    }
}

public class CreateForumReportRequest
{
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Reason { get; set; }
}

public class ForumReportCreatedDto
{
    public string Id { get; set; } = "";
}
