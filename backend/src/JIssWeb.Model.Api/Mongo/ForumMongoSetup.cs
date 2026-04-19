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

        var likes = db.GetCollection<ForumPostLikeRecord>(LikesCollectionName);
        var likeUnique = Builders<ForumPostLikeRecord>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.UserSubId);
        likes.Indexes.CreateOne(new CreateIndexModel<ForumPostLikeRecord>(likeUnique, new CreateIndexOptions { Unique = true }));

        var favorites = db.GetCollection<ForumPostFavoriteRecord>(FavoritesCollectionName);
        var favUnique = Builders<ForumPostFavoriteRecord>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.UserSubId);
        favorites.Indexes.CreateOne(new CreateIndexModel<ForumPostFavoriteRecord>(favUnique, new CreateIndexOptions { Unique = true }));

        var announcements = db.GetCollection<ForumAnnouncementRecord>(AnnouncementsCollectionName);
        var annList = Builders<ForumAnnouncementRecord>.IndexKeys.Descending(x => x.Pinned).Descending(x => x.PublishedAtUtc);
        announcements.Indexes.CreateOne(new CreateIndexModel<ForumAnnouncementRecord>(annList));
    }
}
