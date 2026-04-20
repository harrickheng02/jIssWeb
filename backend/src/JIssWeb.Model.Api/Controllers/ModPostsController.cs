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
    private readonly ILogger<ModPostsController> _logger;

    public ModPostsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boardOptions,
        ForumModerationAccessService access,
        ILogger<ModPostsController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _boards = boardOptions;
        _access = access;
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

