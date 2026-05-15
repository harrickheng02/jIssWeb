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
[Route("api/mod/posts")]
public class ModPostsController : ControllerBase
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumModerationAuditRecord> _audit;
    private readonly ForumModerationAccessService _access;
    private readonly IOptions<ForumBoardsOptions> _boards;
    private readonly ForumModerationDeleteService _delete;
    private readonly ILogger<ModPostsController> _logger;

    public ModPostsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boardOptions,
        ForumModerationAccessService access,
        ForumModerationDeleteService delete,
        ILogger<ModPostsController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _boards = boardOptions;
        _access = access;
        _delete = delete;
        _logger = logger;
    }

    [HttpPost("{postId}/sticky")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<SetStickyResultDto>>> SetSticky(string postId, [FromBody] SetStickyRequest request)
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<SetStickyResultDto>.Fail("未授权", "UNAUTHORIZED"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null)
            return NotFound(ApiResult<SetStickyResultDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModeratePostAsModerator(User, sub, post))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<SetStickyResultDto>.Fail("无权操作该帖子", "FORBIDDEN"));
        }

        if (post.IsSticky == request.IsSticky)
        {
            return Ok(ApiResult<SetStickyResultDto>.Ok(new SetStickyResultDto
            {
                PostId = postId,
                IsSticky = post.IsSticky,
            }));
        }

        var now = DateTime.UtcNow;
        var oldIsSticky = post.IsSticky;
        var oldStickyAt = post.StickyAtUtc;
        var oldStickyBy = post.StickyBySub;

        var update = request.IsSticky
            ? Builders<ForumPostRecord>.Update
                .Set(x => x.IsSticky, true)
                .Set(x => x.StickyAtUtc, now)
                .Set(x => x.StickyBySub, sub)
            : Builders<ForumPostRecord>.Update
                .Set(x => x.IsSticky, false)
                .Set(x => x.StickyAtUtc, (DateTime?)null)
                .Set(x => x.StickyBySub, (string?)null);

        var upd = await _posts.UpdateOneAsync(x => x.Id == postId, update);
        if (upd.MatchedCount != 1)
            return NotFound(ApiResult<SetStickyResultDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var action = request.IsSticky ? "post.setSticky" : "post.unsetSticky";
        var meta = new Dictionary<string, object>
        {
            ["board"] = post.Board,
            ["oldIsSticky"] = oldIsSticky,
            ["newIsSticky"] = request.IsSticky,
        };
        var boardIdResolved = ForumBoardIdLookup.ResolveBoardIdFromTitle(_boards.Value, post.Board);
        if (!string.IsNullOrEmpty(boardIdResolved))
            meta["boardId"] = boardIdResolved;

        var audit = new ForumModerationAuditRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TargetType = "post",
            TargetId = postId,
            Action = action,
            OperatorSub = sub,
            OccurredAtUtc = now,
            Metadata = meta,
        };

        try
        {
            await _audit.InsertOneAsync(audit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write moderation audit for {PostId}", postId);

            var rollback = oldIsSticky
                ? Builders<ForumPostRecord>.Update
                    .Set(x => x.IsSticky, true)
                    .Set(x => x.StickyAtUtc, oldStickyAt)
                    .Set(x => x.StickyBySub, oldStickyBy)
                : Builders<ForumPostRecord>.Update
                    .Set(x => x.IsSticky, false)
                    .Set(x => x.StickyAtUtc, (DateTime?)null)
                    .Set(x => x.StickyBySub, (string?)null);

            var rb = await _posts.UpdateOneAsync(x => x.Id == postId, rollback);
            if (rb.MatchedCount != 1)
                _logger.LogError(
                    "Sticky rollback did not match post {PostId}; database state may diverge (sticky changed but audit missing). Reconcile manually if needed.",
                    postId);

            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResult<SetStickyResultDto>.Fail("写入审计失败", "AUDIT_WRITE_FAILED"));
        }

        return Ok(ApiResult<SetStickyResultDto>.Ok(new SetStickyResultDto
        {
            PostId = postId,
            IsSticky = request.IsSticky,
        }));
    }

    [HttpPost("{postId}/replies-locked")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<SetRepliesLockedResultDto>>> SetRepliesLocked(
        string postId,
        [FromBody] SetRepliesLockedRequest request,
        CancellationToken ct = default)
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<SetRepliesLockedResultDto>.Fail("未授权", "UNAUTHORIZED"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync(ct);
        if (post is null)
            return NotFound(ApiResult<SetRepliesLockedResultDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModeratePostAsModerator(User, sub, post))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<SetRepliesLockedResultDto>.Fail("无权操作该帖子", "FORBIDDEN"));
        }

        if (post.RepliesLocked == request.RepliesLocked)
        {
            return Ok(ApiResult<SetRepliesLockedResultDto>.Ok(new SetRepliesLockedResultDto
            {
                PostId = postId,
                RepliesLocked = post.RepliesLocked,
            }));
        }

        var now = DateTime.UtcNow;
        var oldLocked = post.RepliesLocked;
        var oldAt = post.RepliesLockedAtUtc;
        var oldBy = post.RepliesLockedBySub;

        var update = request.RepliesLocked
            ? Builders<ForumPostRecord>.Update
                .Set(x => x.RepliesLocked, true)
                .Set(x => x.RepliesLockedAtUtc, now)
                .Set(x => x.RepliesLockedBySub, sub)
            : Builders<ForumPostRecord>.Update
                .Set(x => x.RepliesLocked, false)
                .Set(x => x.RepliesLockedAtUtc, (DateTime?)null)
                .Set(x => x.RepliesLockedBySub, (string?)null);

        var upd = await _posts.UpdateOneAsync(x => x.Id == postId, update, cancellationToken: ct);
        if (upd.MatchedCount != 1)
            return NotFound(ApiResult<SetRepliesLockedResultDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var action = request.RepliesLocked ? "post.lockReplies" : "post.unlockReplies";
        var meta = new Dictionary<string, object>
        {
            ["board"] = post.Board,
            ["oldRepliesLocked"] = oldLocked,
            ["newRepliesLocked"] = request.RepliesLocked,
        };
        var boardIdResolved = ForumBoardIdLookup.ResolveBoardIdFromTitle(_boards.Value, post.Board);
        if (!string.IsNullOrEmpty(boardIdResolved))
            meta["boardId"] = boardIdResolved;

        var audit = new ForumModerationAuditRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TargetType = "post",
            TargetId = postId,
            Action = action,
            OperatorSub = sub,
            OccurredAtUtc = now,
            Metadata = meta,
        };

        try
        {
            await _audit.InsertOneAsync(audit, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write moderation audit for replies-locked {PostId}", postId);

            var rollback = oldLocked
                ? Builders<ForumPostRecord>.Update
                    .Set(x => x.RepliesLocked, true)
                    .Set(x => x.RepliesLockedAtUtc, oldAt)
                    .Set(x => x.RepliesLockedBySub, oldBy)
                : Builders<ForumPostRecord>.Update
                    .Set(x => x.RepliesLocked, false)
                    .Set(x => x.RepliesLockedAtUtc, (DateTime?)null)
                    .Set(x => x.RepliesLockedBySub, (string?)null);

            var rb = await _posts.UpdateOneAsync(x => x.Id == postId, rollback, cancellationToken: ct);
            if (rb.MatchedCount != 1)
                _logger.LogError(
                    "Replies-locked rollback did not match post {PostId}; state may diverge.",
                    postId);

            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResult<SetRepliesLockedResultDto>.Fail("写入审计失败", "AUDIT_WRITE_FAILED"));
        }

        return Ok(ApiResult<SetRepliesLockedResultDto>.Ok(new SetRepliesLockedResultDto
        {
            PostId = postId,
            RepliesLocked = request.RepliesLocked,
        }));
    }

    [HttpPost("{postId}/featured")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<SetFeaturedResultDto>>> SetFeatured(
        string postId,
        [FromBody] SetFeaturedRequest request,
        CancellationToken ct = default)
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<SetFeaturedResultDto>.Fail("未授权", "UNAUTHORIZED"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync(ct);
        if (post is null)
            return NotFound(ApiResult<SetFeaturedResultDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModeratePostAsModerator(User, sub, post))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<SetFeaturedResultDto>.Fail("无权操作该帖子", "FORBIDDEN"));
        }

        if (post.IsFeatured == request.IsFeatured)
        {
            return Ok(ApiResult<SetFeaturedResultDto>.Ok(new SetFeaturedResultDto
            {
                PostId = postId,
                IsFeatured = post.IsFeatured,
            }));
        }

        var now = DateTime.UtcNow;
        var oldIsFeatured = post.IsFeatured;
        var oldFeaturedAt = post.FeaturedAtUtc;
        var oldFeaturedBy = post.FeaturedBySub;

        var update = request.IsFeatured
            ? Builders<ForumPostRecord>.Update
                .Set(x => x.IsFeatured, true)
                .Set(x => x.FeaturedAtUtc, now)
                .Set(x => x.FeaturedBySub, sub)
            : Builders<ForumPostRecord>.Update
                .Set(x => x.IsFeatured, false)
                .Set(x => x.FeaturedAtUtc, (DateTime?)null)
                .Set(x => x.FeaturedBySub, (string?)null);

        var upd = await _posts.UpdateOneAsync(x => x.Id == postId, update, cancellationToken: ct);
        if (upd.MatchedCount != 1)
            return NotFound(ApiResult<SetFeaturedResultDto>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var action = request.IsFeatured ? "post.setFeatured" : "post.unsetFeatured";
        var meta = new Dictionary<string, object>
        {
            ["board"] = post.Board,
            ["oldIsFeatured"] = oldIsFeatured,
            ["newIsFeatured"] = request.IsFeatured,
        };
        var boardIdResolved = ForumBoardIdLookup.ResolveBoardIdFromTitle(_boards.Value, post.Board);
        if (!string.IsNullOrEmpty(boardIdResolved))
            meta["boardId"] = boardIdResolved;

        var audit = new ForumModerationAuditRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TargetType = "post",
            TargetId = postId,
            Action = action,
            OperatorSub = sub,
            OccurredAtUtc = now,
            Metadata = meta,
        };

        try
        {
            await _audit.InsertOneAsync(audit, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write moderation audit for featured {PostId}", postId);

            var rollback = oldIsFeatured
                ? Builders<ForumPostRecord>.Update
                    .Set(x => x.IsFeatured, true)
                    .Set(x => x.FeaturedAtUtc, oldFeaturedAt)
                    .Set(x => x.FeaturedBySub, oldFeaturedBy)
                : Builders<ForumPostRecord>.Update
                    .Set(x => x.IsFeatured, false)
                    .Set(x => x.FeaturedAtUtc, (DateTime?)null)
                    .Set(x => x.FeaturedBySub, (string?)null);

            var rb = await _posts.UpdateOneAsync(x => x.Id == postId, rollback, cancellationToken: ct);
            if (rb.MatchedCount != 1)
                _logger.LogError(
                    "Featured rollback did not match post {PostId}; database state may diverge.",
                    postId);

            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResult<SetFeaturedResultDto>.Fail("写入审计失败", "AUDIT_WRITE_FAILED"));
        }

        return Ok(ApiResult<SetFeaturedResultDto>.Ok(new SetFeaturedResultDto
        {
            PostId = postId,
            IsFeatured = request.IsFeatured,
        }));
    }

    [HttpDelete("{postId}")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<string>>> DeletePost(string postId, CancellationToken ct)
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<string>.Fail("未授权", "UNAUTHORIZED"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync(ct);
        if (post is null)
            return NotFound(ApiResult<string>.Fail("帖子不存在或已删除", "NOT_FOUND"));

        var role = User.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModeratePostAsModerator(User, sub, post))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权操作该帖子", "FORBIDDEN"));
        }

        var outcome = await _delete.TryDeletePostAsync(User, sub, postId, ct);
        if (outcome == ModerationDeletionOutcome.NotFound)
            return NotFound(ApiResult<string>.Fail("帖子不存在或已删除", "NOT_FOUND"));
        if (outcome == ModerationDeletionOutcome.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权操作该帖子", "FORBIDDEN"));

        var now = DateTime.UtcNow;
        var meta = new Dictionary<string, object> { ["board"] = post.Board };
        var boardIdResolved = ForumBoardIdLookup.ResolveBoardIdFromTitle(_boards.Value, post.Board);
        if (!string.IsNullOrEmpty(boardIdResolved))
            meta["boardId"] = boardIdResolved;

        try
        {
            await _audit.InsertOneAsync(new ForumModerationAuditRecord
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = postId,
                Action = "post.modDelete",
                OperatorSub = sub,
                OccurredAtUtc = now,
                Metadata = meta,
            }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit failed after moderator delete post {PostId}; content already deleted", postId);
        }

        return Ok(ApiResult<string>.Ok("ok"));
    }

    private string? TryGetSub()
    {
        try { return User.GetUserId(); }
        catch (UnauthorizedAccessException) { return null; }
    }
}

public class SetStickyRequest
{
    public bool IsSticky { get; set; }
}

public class SetStickyResultDto
{
    public string PostId { get; set; } = "";
    public bool IsSticky { get; set; }
}

public class SetRepliesLockedRequest
{
    public bool RepliesLocked { get; set; }
}

public class SetRepliesLockedResultDto
{
    public string PostId { get; set; } = "";
    public bool RepliesLocked { get; set; }
}

public class SetFeaturedRequest
{
    public bool IsFeatured { get; set; }
}

public class SetFeaturedResultDto
{
    public string PostId { get; set; } = "";
    public bool IsFeatured { get; set; }
}

