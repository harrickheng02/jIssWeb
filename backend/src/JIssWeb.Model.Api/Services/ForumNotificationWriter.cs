using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public interface IForumNotificationWriter
{
    /// <summary>
    /// 回复帖子时通知帖子作者（回复者与帖子作者相同时跳过）。
    /// </summary>
    Task WriteReplyNotificationAsync(
        ForumPostRecord post,
        ForumReplyRecord reply,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 永久删帖时级联删除该帖下所有通知记录。
    /// </summary>
    Task DeleteByPostAsync(
        string postId,
        CancellationToken cancellationToken = default);
}

public class ForumNotificationWriter : IForumNotificationWriter
{
    private readonly IMongoCollection<InAppNotificationRecord> _notifications;
    private readonly ILogger<ForumNotificationWriter> _logger;

    public ForumNotificationWriter(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ILogger<ForumNotificationWriter> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _notifications = db.GetCollection<InAppNotificationRecord>(
            ForumMongoSetup.NotificationsCollectionName);
        _logger = logger;
    }

    public async Task WriteReplyNotificationAsync(
        ForumPostRecord post,
        ForumReplyRecord reply,
        CancellationToken cancellationToken = default)
    {
        // 回复者与帖子作者相同时不产生通知
        if (string.Equals(post.AuthorSubId, reply.AuthorSubId, StringComparison.Ordinal))
            return;

        var notif = new InAppNotificationRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            RecipientSubId = post.AuthorSubId,
            Type = InAppNotificationTypes.ReplyToPost,
            PostId = post.Id,
            ReplyId = reply.Id,
            ActorSubId = reply.AuthorSubId,
            PostTitle = post.Title,
            ReadAtUtc = null,
            CreatedAtUtc = reply.CreatedAtUtc,
        };

        try
        {
            await _notifications.InsertOneAsync(notif, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            _logger.LogDebug(ex, "Skipped duplicate notification for reply {ReplyId}", reply.Id);
        }
    }

    public async Task DeleteByPostAsync(
        string postId,
        CancellationToken cancellationToken = default)
    {
        await _notifications.DeleteManyAsync(
            x => x.PostId == postId,
            cancellationToken: cancellationToken);
    }
}
