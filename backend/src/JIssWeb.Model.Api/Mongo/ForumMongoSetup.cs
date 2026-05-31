using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using Microsoft.Extensions.Logging;
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
    public const string TagsCollectionName = "forum_tags";

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

        lock (BsonRegistrationLock)
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(ForumTagRecord)))
                BsonClassMap.RegisterClassMap<ForumTagRecord>(cm => cm.AutoMap());
        }

        var tags = db.GetCollection<ForumTagRecord>(TagsCollectionName);
        var tagSlugUnique = Builders<ForumTagRecord>.IndexKeys.Ascending(x => x.Slug);
        tags.Indexes.CreateOne(new CreateIndexModel<ForumTagRecord>(tagSlugUnique, new CreateIndexOptions { Unique = true, Name = "uniq_slug" }));
        var tagStatusUseCount = Builders<ForumTagRecord>.IndexKeys.Ascending(x => x.Status).Descending(x => x.UseCount);
        tags.Indexes.CreateOne(new CreateIndexModel<ForumTagRecord>(tagStatusUseCount, new CreateIndexOptions { Name = "status_usecount" }));
        var tagNameText = Builders<ForumTagRecord>.IndexKeys.Ascending(x => x.Name);
        tags.Indexes.CreateOne(new CreateIndexModel<ForumTagRecord>(tagNameText, new CreateIndexOptions { Name = "name_asc" }));

        var stateCreated = Builders<ForumPostRecord>.IndexKeys.Ascending(x => x.State).Descending(x => x.CreatedAtUtc);
        posts.Indexes.CreateOne(new CreateIndexModel<ForumPostRecord>(stateCreated, new CreateIndexOptions { Name = "state_created" }));
        var authorState = Builders<ForumPostRecord>.IndexKeys.Ascending(x => x.AuthorSubId).Ascending(x => x.State);
        posts.Indexes.CreateOne(new CreateIndexModel<ForumPostRecord>(authorState, new CreateIndexOptions { Name = "author_state" }));

        var replyStatePost = Builders<ForumReplyRecord>.IndexKeys.Ascending(x => x.State).Ascending(x => x.PostId);
        replies.Indexes.CreateOne(new CreateIndexModel<ForumReplyRecord>(replyStatePost, new CreateIndexOptions { Name = "reply_state_post" }));
    }

    public static async Task EnsureStateFieldAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var mongo = scope.ServiceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
        var db = client.GetDatabase(mongo.DatabaseName);

        var missingStateFilter = new MongoDB.Bson.BsonDocument("State", new MongoDB.Bson.BsonDocument("$exists", false));
        var setPublished = new MongoDB.Bson.BsonDocument("$set", new MongoDB.Bson.BsonDocument("State", "published"));

        var posts = db.GetCollection<MongoDB.Bson.BsonDocument>(PostsCollectionName);
        await posts.UpdateManyAsync(missingStateFilter, setPublished);

        var replies = db.GetCollection<MongoDB.Bson.BsonDocument>(RepliesCollectionName);
        await replies.UpdateManyAsync(missingStateFilter, setPublished);
    }

    // 官方初始标签：name → (slug, 可选说明)
    private static readonly (string Name, string Slug, string? Description)[] InitialTags =
    [
        ("问答",    "问答",    "提问与解答"),
        ("分享",    "分享",    "经验、资源与心得分享"),
        ("讨论",    "讨论",    "话题讨论与观点交流"),
        ("求助",    "求助",    "遇到问题？来这里求助"),
        ("公告",    "公告",    "官方公告与重要通知"),
        ("活动",    "活动",    "社区活动与赛事"),
        ("资源",    "资源",    "学习资源、工具与推荐"),
        ("教程",    "教程",    "入门教程与实战指南"),
        ("技术",    "技术",    "技术话题"),
        ("前端",    "前端",    "前端开发相关"),
        ("后端",    "后端",    "后端开发相关"),
        ("移动开发", "移动开发", "iOS / Android / 跨端"),
        ("开源",    "开源",    "开源项目与贡献"),
        ("工具",    "工具",    "效率工具与开发环境"),
        ("职场",    "职场",    "求职、晋升、职业发展"),
        ("新手",    "新手",    "新人友好，欢迎提问"),
        ("项目展示", "项目展示", "展示你的作品与项目"),
        ("反馈",    "反馈",    "对本社区的建议与反馈"),
    ];

    public static async Task SeedInitialTagsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var mongo = scope.ServiceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
        var db = client.GetDatabase(mongo.DatabaseName);
        var tags = db.GetCollection<ForumTagRecord>(TagsCollectionName);

        // 若集合已有数据则跳过，避免重复 seed
        if (await tags.CountDocumentsAsync(FilterDefinition<ForumTagRecord>.Empty) > 0)
            return;

        var now = DateTime.UtcNow;
        var records = InitialTags.Select(t => new ForumTagRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Name = t.Name,
            Slug = t.Name.Trim().ToLowerInvariant(),
            Description = t.Description,
            Status = ForumTagStatuses.Active,
            UseCount = 0,
            CreatedAtUtc = now,
            CreatedBySub = "system",
        }).ToList();

        try
        {
            await tags.InsertManyAsync(records);
        }
        catch (Exception ex)
        {
            var loggerFactory = services.GetService<ILoggerFactory>();
            loggerFactory?.CreateLogger(nameof(ForumMongoSetup))
                .LogWarning(ex, "初始标签 seed 失败，已跳过（可在下次启动时重试）");
        }
    }
}
