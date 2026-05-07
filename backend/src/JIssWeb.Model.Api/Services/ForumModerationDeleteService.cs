using System.Security.Claims;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public enum ModerationDeletionOutcome
{
    Success,
    NotFound,
    Forbidden,
}

/// <summary>Hard-delete forum posts/replies under moderator/admin authorization.</summary>
public sealed class ForumModerationDeleteService
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IMongoCollection<InAppNotificationRecord> _notifications;
    private readonly ForumModerationAccessService _access;
    private readonly ForumEngagementService _engagement;
    private readonly ILogger<ForumModerationDeleteService> _logger;

    public ForumModerationDeleteService(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumModerationAccessService access,
        ForumEngagementService engagement,
        ILogger<ForumModerationDeleteService> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _notifications = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        _access = access;
        _engagement = engagement;
        _logger = logger;
    }

    public async Task<ModerationDeletionOutcome> TryDeletePostAsync(
        ClaimsPrincipal user,
        string operatorSub,
        string postId,
        CancellationToken ct = default)
    {
        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync(ct);
        if (post is null)
            return ModerationDeletionOutcome.NotFound;

        var role = user.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModeratePostAsModerator(user, operatorSub, post))
                return ModerationDeletionOutcome.Forbidden;
        }

        try
        {
            await _engagement.RemoveAllForPostAsync(postId);
            await _replies.DeleteManyAsync(x => x.PostId == postId, ct);
            await _notifications.DeleteManyAsync(x => x.PostId == postId, ct);
            var dp = await _posts.DeleteOneAsync(x => x.Id == postId, ct);
            if (dp.DeletedCount != 1)
                return ModerationDeletionOutcome.NotFound;
            return ModerationDeletionOutcome.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to moderator-delete post {PostId}", postId);
            throw;
        }
    }

    public async Task<ModerationDeletionOutcome> TryDeleteReplyAsync(
        ClaimsPrincipal user,
        string operatorSub,
        string replyId,
        CancellationToken ct = default)
    {
        var reply = await _replies.Find(x => x.Id == replyId).FirstOrDefaultAsync(ct);
        if (reply is null)
            return ModerationDeletionOutcome.NotFound;

        var parent = await _posts.Find(x => x.Id == reply.PostId).FirstOrDefaultAsync(ct);
        if (parent is null)
            return ModerationDeletionOutcome.NotFound;

        var role = user.GetForumPrincipalRole();
        if (role == ForumPrincipalRole.Moderator)
        {
            if (!_access.CanModeratePostAsModerator(user, operatorSub, parent))
                return ModerationDeletionOutcome.Forbidden;
        }

        try
        {
            await _notifications.DeleteManyAsync(x => x.ReplyId == replyId, ct);

            var dr = await _replies.DeleteOneAsync(x => x.Id == replyId, ct);
            if (dr.DeletedCount != 1)
                return ModerationDeletionOutcome.NotFound;

            await _posts.UpdateOneAsync(
                x => x.Id == reply.PostId && x.CommentCount > 0,
                Builders<ForumPostRecord>.Update.Inc(x => x.CommentCount, -1),
                cancellationToken: ct);

            return ModerationDeletionOutcome.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to moderator-delete reply {ReplyId}", replyId);
            throw;
        }
    }
}
