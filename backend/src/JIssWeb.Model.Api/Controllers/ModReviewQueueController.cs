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

/// <summary>
/// AI 版主阶段一：复审队列。运营者在这里对 AI 判定结果做最终处置——
/// approve 恢复公开、reject 删除；两者都写审计。入队本身不写审计（AI 量大，会淹没版主时间线）。
/// </summary>
[ApiController]
[Route("api/mod/review-queue")]
public class ModReviewQueueController : ControllerBase
{
    private const string SystemActorSub = "system:ai-moderator";

    private readonly IMongoCollection<ForumModerationQueueRecord> _queue;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IMongoCollection<ForumModerationAuditRecord> _audit;
    private readonly ForumModerationAccessService _access;
    private readonly IOptions<ForumBoardsOptions> _boards;

    public ModReviewQueueController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boardOptions,
        ForumModerationAccessService access)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _queue = db.GetCollection<ForumModerationQueueRecord>(ForumMongoSetup.ModerationQueueCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _access = access;
        _boards = boardOptions;
    }

    [HttpGet]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<PagedReviewQueueDto>>> List(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedReviewQueueDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        var sub = TryGetSub();
        if (sub is null) return Unauthorized(ApiResult<PagedReviewQueueDto>.Fail("未授权", "UNAUTHORIZED"));

        var filter = Builders<ForumModerationQueueRecord>.Filter.Empty;
        var normalizedStatus = (status ?? "").Trim();
        if (normalizedStatus.Length > 0)
        {
            if (!IsKnownStatus(normalizedStatus))
                return BadRequest(ApiResult<PagedReviewQueueDto>.Fail("状态无效", "INVALID_STATUS"));
            filter &= Builders<ForumModerationQueueRecord>.Filter.Eq(x => x.Status, normalizedStatus);
        }

        if (User.GetForumPrincipalRole() == ForumPrincipalRole.Moderator)
        {
            var scope = _access.GetModeratorBoardIdScope(User, sub);
            if (scope is null || scope.Count == 0)
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<PagedReviewQueueDto>.Fail("无权访问", "FORBIDDEN"));
            // 队列行的 BoardId 可能是板块 id，也可能是板块标题（回复直写时只有标题）。
            var tokens = scope
                .Concat(scope.Select(id => ForumBoardIdLookup.ResolveConfiguredBoardTitle(_boards.Value, id) ?? id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            filter &= Builders<ForumModerationQueueRecord>.Filter.In(x => x.BoardId, tokens);
        }

        var total = await _queue.CountDocumentsAsync(filter, cancellationToken: ct);
        var rows = await _queue.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var dtos = await ToDtosAsync(rows, ct);
        return Ok(ApiResult<PagedReviewQueueDto>.Ok(new PagedReviewQueueDto
        {
            Items = dtos,
            TotalCount = (int)total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpPost("{id}/approve")]
    [Authorize]
    [RequireForumModerator]
    public Task<ActionResult<ApiResult<ReviewQueueItemDto>>> Approve(string id, CancellationToken ct = default)
        => DecideAsync(id, approve: true, ct);

    [HttpPost("{id}/reject")]
    [Authorize]
    [RequireForumModerator]
    public Task<ActionResult<ApiResult<ReviewQueueItemDto>>> Reject(string id, CancellationToken ct = default)
        => DecideAsync(id, approve: false, ct);

    private async Task<ActionResult<ApiResult<ReviewQueueItemDto>>> DecideAsync(string id, bool approve, CancellationToken ct)
    {
        var sub = TryGetSub();
        if (sub is null) return Unauthorized(ApiResult<ReviewQueueItemDto>.Fail("未授权", "UNAUTHORIZED"));

        var row = await _queue.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        if (row is null) return NotFound(ApiResult<ReviewQueueItemDto>.Fail("队列项不存在", "NOT_FOUND"));

        if (row.Status is ForumModerationQueueStatuses.Approved or ForumModerationQueueStatuses.Rejected)
            return Conflict(ApiResult<ReviewQueueItemDto>.Fail("该项已处置", "ALREADY_HANDLED"));

        if (User.GetForumPrincipalRole() == ForumPrincipalRole.Moderator
            && !_access.CanModerateBoardIdAsModerator(User, sub, row.BoardId)
            && !_access.CanModerateBoardTitleAsModerator(User, sub, row.BoardId))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<ReviewQueueItemDto>.Fail("无权处置该内容", "FORBIDDEN"));

        var now = DateTime.UtcNow;
        var isPost = string.Equals(row.TargetType, "post", StringComparison.OrdinalIgnoreCase);

        if (isPost)
        {
            var update = approve
                ? Builders<ForumPostRecord>.Update.Set(x => x.State, ForumContentStates.Published)
                : Builders<ForumPostRecord>.Update
                    .Set(x => x.State, ForumContentStates.Deleted)
                    .Set(x => x.DeletedAtUtc, now)
                    .Set(x => x.DeletedBySub, SystemActorSub);
            await _posts.UpdateOneAsync(x => x.Id == row.TargetId, update, cancellationToken: ct);
        }
        else
        {
            var update = approve
                ? Builders<ForumReplyRecord>.Update.Set(x => x.State, ForumContentStates.Published)
                : Builders<ForumReplyRecord>.Update
                    .Set(x => x.State, ForumContentStates.Deleted)
                    .Set(x => x.DeletedAtUtc, now)
                    .Set(x => x.DeletedBySub, SystemActorSub);
            await _replies.UpdateOneAsync(x => x.Id == row.TargetId, update, cancellationToken: ct);

            // 通过待审回复后它才进入公开面，评论数同步补回。
            if (approve && !string.IsNullOrWhiteSpace(row.PostId))
                await _posts.UpdateOneAsync(x => x.Id == row.PostId,
                    Builders<ForumPostRecord>.Update.Inc(x => x.CommentCount, 1), cancellationToken: ct);
        }

        await _queue.UpdateOneAsync(x => x.Id == id,
            Builders<ForumModerationQueueRecord>.Update
                .Set(x => x.Status, approve ? ForumModerationQueueStatuses.Approved : ForumModerationQueueStatuses.Rejected)
                .Set(x => x.HandledBySub, sub)
                .Set(x => x.HandledAtUtc, now),
            cancellationToken: ct);

        var meta = new Dictionary<string, object>
        {
            ["source"] = row.Source,
            ["status"] = row.Status,
        };
        if (!string.IsNullOrWhiteSpace(row.Verdict)) meta["verdict"] = row.Verdict;
        if (row.Confidence is double confidence) meta["confidence"] = confidence;
        if (!string.IsNullOrWhiteSpace(row.Reason)) meta["reason"] = row.Reason;
        if (!string.IsNullOrWhiteSpace(row.BoardId)) meta["boardId"] = row.BoardId;
        if (!string.IsNullOrWhiteSpace(row.PostId)) meta["postId"] = row.PostId;

        await _audit.InsertOneAsync(new ForumModerationAuditRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TargetType = row.TargetType,
            TargetId = row.TargetId,
            Action = approve ? ModerationAuditActions.ContentApprove : ModerationAuditActions.ContentReject,
            OperatorSub = sub,
            OccurredAtUtc = now,
            Metadata = meta,
        }, cancellationToken: ct);

        row.Status = approve ? ForumModerationQueueStatuses.Approved : ForumModerationQueueStatuses.Rejected;
        row.HandledBySub = sub;
        row.HandledAtUtc = now;
        var dtos = await ToDtosAsync(new[] { row }, ct);
        return Ok(ApiResult<ReviewQueueItemDto>.Ok(dtos[0]));
    }

    private async Task<List<ReviewQueueItemDto>> ToDtosAsync(IReadOnlyList<ForumModerationQueueRecord> rows, CancellationToken ct)
    {
        var postIds = rows.Where(x => string.Equals(x.TargetType, "post", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetId).Distinct().ToList();
        var replyIds = rows.Where(x => string.Equals(x.TargetType, "reply", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetId).Distinct().ToList();

        var posts = postIds.Count == 0
            ? new Dictionary<string, ForumPostRecord>()
            : (await _posts.Find(Builders<ForumPostRecord>.Filter.In(x => x.Id, postIds)).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x);
        var replies = replyIds.Count == 0
            ? new Dictionary<string, ForumReplyRecord>()
            : (await _replies.Find(Builders<ForumReplyRecord>.Filter.In(x => x.Id, replyIds)).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x);

        return rows.Select(row =>
        {
            var dto = new ReviewQueueItemDto
            {
                Id = row.Id,
                TargetType = row.TargetType,
                TargetId = row.TargetId,
                PostId = row.PostId,
                BoardId = row.BoardId,
                AuthorSubId = row.AuthorSubId,
                Source = row.Source,
                Status = row.Status,
                Verdict = row.Verdict,
                Confidence = row.Confidence,
                Reason = row.Reason,
                CreatedAtUtc = row.CreatedAtUtc,
                HandledBySub = row.HandledBySub,
                HandledAtUtc = row.HandledAtUtc,
            };
            if (string.Equals(row.TargetType, "post", StringComparison.OrdinalIgnoreCase)
                && posts.TryGetValue(row.TargetId, out var post))
            {
                dto.Title = post.Title;
                dto.ContentExcerpt = Excerpt(post.Body);
                dto.ContentState = post.State;
            }
            else if (replies.TryGetValue(row.TargetId, out var reply))
            {
                dto.ContentExcerpt = Excerpt(reply.Body);
                dto.ContentState = reply.State;
            }
            return dto;
        }).ToList();
    }

    private static string Excerpt(string? body)
    {
        var text = (body ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
        return text.Length <= 200 ? text : text[..200];
    }

    private static bool IsKnownStatus(string status) =>
        status is ForumModerationQueueStatuses.Pending
            or ForumModerationQueueStatuses.Flagged
            or ForumModerationQueueStatuses.Approved
            or ForumModerationQueueStatuses.Rejected;

    private string? TryGetSub()
    {
        try { return User.GetUserId(); }
        catch (UnauthorizedAccessException) { return null; }
    }
}

public class PagedReviewQueueDto
{
    public List<ReviewQueueItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ReviewQueueItemDto
{
    public string Id { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string PostId { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Verdict { get; set; }
    public double? Confidence { get; set; }
    public string? Reason { get; set; }
    public string? Title { get; set; }
    public string? ContentExcerpt { get; set; }
    public string? ContentState { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? HandledBySub { get; set; }
    public DateTime? HandledAtUtc { get; set; }
}
