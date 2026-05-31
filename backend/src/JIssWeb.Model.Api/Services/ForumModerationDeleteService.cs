using System.Security.Claims;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public enum ModerationDeletionOutcome
{
    Success,
    NotFound,
    Forbidden,
}

/// <summary>Soft-delete forum posts/replies under moderator/admin authorization.</summary>
public sealed class ForumModerationDeleteService
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IMongoCollection<InAppNotificationRecord> _notifications;
    private readonly IMongoCollection<ForumTagRecord> _tagRecords;
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
        _tagRecords = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
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
        if (role == ForumPrincipalRole.Moderator
            && !string.Equals(post.AuthorSubId, operatorSub, StringComparison.Ordinal))
        {
            if (!_access.CanModeratePostAsModerator(user, operatorSub, post))
                return ModerationDeletionOutcome.Forbidden;
        }

        try
        {
            var now = DateTime.UtcNow;
            var softDeleteUpdate = Builders<ForumPostRecord>.Update
                .Set(x => x.State, "deleted")
                .Set(x => x.DeletedAtUtc, now)
                .Set(x => x.DeletedBySub, operatorSub);
            var ur = await _posts.UpdateOneAsync(x => x.Id == postId, softDeleteUpdate, cancellationToken: ct);
            if (ur.MatchedCount != 1)
                return ModerationDeletionOutcome.NotFound;

            // Tags UseCount -1 delta（软删与硬删行为一致）
            if (post.Tags?.Count > 0)
                await _tagRecords.UpdateManyAsync(
                    Builders<ForumTagRecord>.Filter.And(
                        Builders<ForumTagRecord>.Filter.In(x => x.Name, post.Tags),
                        Builders<ForumTagRecord>.Filter.Gt(x => x.UseCount, 0)),
                    Builders<ForumTagRecord>.Update.Inc(x => x.UseCount, -1),
                    cancellationToken: ct);

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
            var now = DateTime.UtcNow;
            var softDeleteReply = Builders<ForumReplyRecord>.Update
                .Set(x => x.State, "deleted")
                .Set(x => x.DeletedAtUtc, now)
                .Set(x => x.DeletedBySub, operatorSub);
            var ur = await _replies.UpdateOneAsync(x => x.Id == replyId, softDeleteReply, cancellationToken: ct);
            if (ur.MatchedCount != 1)
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
