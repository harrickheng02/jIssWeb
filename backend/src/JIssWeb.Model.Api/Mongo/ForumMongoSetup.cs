using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Mongo;

public static class ForumMongoSetup
{
    public const string PostsCollectionName = "forum_posts";
    public const string RepliesCollectionName = "forum_replies";

    public static void EnsureIndexes(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var mongo = scope.ServiceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
        var db = client.GetDatabase(mongo.DatabaseName);

        var posts = db.GetCollection<ForumPostRecord>(PostsCollectionName);
        var created = Builders<ForumPostRecord>.IndexKeys.Descending(x => x.CreatedAtUtc);
        posts.Indexes.CreateOne(new CreateIndexModel<ForumPostRecord>(created));

        var replies = db.GetCollection<ForumReplyRecord>(RepliesCollectionName);
        var replyKeys = Builders<ForumReplyRecord>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.CreatedAtUtc);
        replies.Indexes.CreateOne(new CreateIndexModel<ForumReplyRecord>(replyKeys));
    }
}
