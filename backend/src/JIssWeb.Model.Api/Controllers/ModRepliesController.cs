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
    private readonly IMongoCollection<ForumModerationAuditRecord> _audit;
    private readonly ForumModerationAccessService _access;
    private readonly ForumModerationDeleteService _delete;
    private readonly IOptions<ForumBoardsOptions> _boards;
    private readonly ILogger<ModRepliesController> _logger;

    public ModRepliesController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumModerationAccessService access,
        ForumModerationDeleteService delete,
        IOptions<ForumBoardsOptions> boards,
        ILogger<ModRepliesController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        _access = access;
        _delete = delete;
        _boards = boards;
        _logger = logger;
    }

    [HttpDelete("{replyId}")]
    [Authorize]
    [RequireForumModerator]
    public async Task<ActionResult<ApiResult<string>>> DeleteReply(string replyId, CancellationToken ct)
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
