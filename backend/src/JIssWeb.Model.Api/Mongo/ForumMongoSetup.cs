using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Mongo;

public static class ForumMongoSetup
{
    private static readonly object BsonRegistrationLock = new();

    public const string PostsCollectionName = "forum_posts";
    public const string RepliesCollectionName = "forum_replies";
    public const string NotificationsCollectionName = "forum_in_app_notifications";
    public const string LikesCollectionName = "forum_post_likes";
    public const string FavoritesCollectionName = "forum_post_favorites";
    public const string AnnouncementsCollectionName = "forum_announcements";
    public const string ModerationAuditCollectionName = "forum_moderation_audit";
    public const string ReportsCollectionName = "forum_reports";

    private static readonly Lazy<(string Title, string AuthorSubId)> PostSearchBsonFields = new(() =>
    {
        lock (BsonRegistrationLock)
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(ForumPostRecord)))
                BsonClassMap.RegisterClassMap<ForumPostRecord>(cm => cm.AutoMap());
        }
        var map = BsonClassMap.LookupClassMap(typeof(ForumPostRecord));
        return (
            map.GetMemberMap(nameof(ForumPostRecord.Title)).ElementName,
            map.GetMemberMap(nameof(ForumPostRecord.AuthorSubId)).ElementName);
    });

    public static (string Title, string AuthorSubId) GetPostSearchBsonFields() => PostSearchBsonFields.Value;

    public static void EnsureIndexes(IServiceProvider services)
    {
        lock (BsonRegistrationLock)
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(ForumAnnouncementRecord)))
                BsonClassMap.RegisterClassMap<ForumAnnouncementRecord>(cm => cm.AutoMap());
            if (!BsonClassMap.IsClassMapRegistered(typeof(ForumReportRecord)))
                BsonClassMap.RegisterClassMap<ForumReportRecord>(cm => cm.AutoMap());
        }

        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var mongo = scope.ServiceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
        var db = client.GetDatabase(mongo.DatabaseName);

        var posts = db.GetCollection<ForumPostRecord>(PostsCollectionName);
        var created = Builders<ForumPostRecord>.IndexKeys.Descending(x => x.CreatedAtUtc);
        posts.Indexes.CreateOne(new CreateIndexModel<ForumPostRecord>(created));
        var byAuthorPosts = Builders<ForumPostRecord>.IndexKeys.Ascending(x => x.AuthorSubId).Descending(x => x.CreatedAtUtc);
        posts.Indexes.CreateOne(new CreateIndexModel<ForumPostRecord>(byAuthorPosts));
        var byTitle = Builders<ForumPostRecord>.IndexKeys.Ascending(x => x.Title);
        posts.Indexes.CreateOne(new CreateIndexModel<ForumPostRecord>(byTitle));
        var featuredIdx = Builders<ForumPostRecord>.IndexKeys.Ascending(x => x.IsFeatured).Descending(x => x.FeaturedAtUtc);
        posts.Indexes.CreateOne(new CreateIndexModel<ForumPostRecord>(featuredIdx));

        var replies = db.GetCollection<ForumReplyRecord>(RepliesCollectionName);
        var replyKeys = Builders<ForumReplyRecord>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.CreatedAtUtc);
        replies.Indexes.CreateOne(new CreateIndexModel<ForumReplyRecord>(replyKeys));
        var byAuthorReplies = Builders<ForumReplyRecord>.IndexKeys.Ascending(x => x.AuthorSubId).Descending(x => x.CreatedAtUtc);
        replies.Indexes.CreateOne(new CreateIndexModel<ForumReplyRecord>(byAuthorReplies));

        var notifications = db.GetCollection<InAppNotificationRecord>(NotificationsCollectionName);
        var byRecipientTime = Builders<InAppNotificationRecord>.IndexKeys.Ascending(x => x.RecipientSubId).Descending(x => x.CreatedAtUtc);
        notifications.Indexes.CreateOne(new CreateIndexModel<InAppNotificationRecord>(byRecipientTime));
        var byReplyId = Builders<InAppNotificationRecord>.IndexKeys.Ascending(x => x.ReplyId);
        notifications.Indexes.CreateOne(
            new CreateIndexModel<InAppNotificationRecord>(byReplyId, new CreateIndexOptions { Unique = true, Sparse = true }));
        var byReportId = Builders<InAppNotificationRecord>.IndexKeys.Ascending(x => x.ReportId);
        notifications.Indexes.CreateOne(
            new CreateIndexModel<InAppNotificationRecord>(byReportId, new CreateIndexOptions { Unique = true, Sparse = true }));

        var likes = db.GetCollection<ForumPostLikeRecord>(LikesCollectionName);
        var likeUnique = Builders<ForumPostLikeRecord>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.UserSubId);
        likes.Indexes.CreateOne(new CreateIndexModel<ForumPostLikeRecord>(likeUnique, new CreateIndexOptions { Unique = true }));

        var favorites = db.GetCollection<ForumPostFavoriteRecord>(FavoritesCollectionName);
        var favUnique = Builders<ForumPostFavoriteRecord>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.UserSubId);
        favorites.Indexes.CreateOne(new CreateIndexModel<ForumPostFavoriteRecord>(favUnique, new CreateIndexOptions { Unique = true }));

        var announcements = db.GetCollection<ForumAnnouncementRecord>(AnnouncementsCollectionName);
        var annList = Builders<ForumAnnouncementRecord>.IndexKeys.Descending(x => x.Pinned).Descending(x => x.PublishedAtUtc);
        announcements.Indexes.CreateOne(new CreateIndexModel<ForumAnnouncementRecord>(annList));

        var moderationAudit = db.GetCollection<ForumModerationAuditRecord>(ModerationAuditCollectionName);
        var auditKeys = Builders<ForumModerationAuditRecord>.IndexKeys
            .Ascending(x => x.TargetType)
            .Ascending(x => x.TargetId)
            .Descending(x => x.OccurredAtUtc);
        moderationAudit.Indexes.CreateOne(new CreateIndexModel<ForumModerationAuditRecord>(auditKeys));

        var reports = db.GetCollection<ForumReportRecord>(ReportsCollectionName);
        var reportListKeys = Builders<ForumReportRecord>.IndexKeys.Ascending(x => x.Status).Descending(x => x.CreatedAtUtc);
        reports.Indexes.CreateOne(new CreateIndexModel<ForumReportRecord>(reportListKeys));
        var reportBoardKeys = Builders<ForumReportRecord>.IndexKeys.Ascending(x => x.BoardId).Descending(x => x.CreatedAtUtc);
        reports.Indexes.CreateOne(new CreateIndexModel<ForumReportRecord>(reportBoardKeys));
        var reportDupKeys = Builders<ForumReportRecord>.IndexKeys
            .Ascending(x => x.ReporterSub)
            .Ascending(x => x.TargetType)
            .Ascending(x => x.TargetId);
        var reportDupOpts = new CreateIndexOptions<ForumReportRecord>
        {
            Unique = true,
            Name = "uniq_pending_reporter_target",
            PartialFilterExpression = Builders<ForumReportRecord>.Filter.Eq(r => r.Status, ForumReportStatuses.Pending),
        };
        reports.Indexes.CreateOne(new CreateIndexModel<ForumReportRecord>(reportDupKeys, reportDupOpts));
        var reportsClosedHandled = Builders<ForumReportRecord>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.HandledAtUtc);
        reports.Indexes.CreateOne(new CreateIndexModel<ForumReportRecord>(reportsClosedHandled));
    }
}
