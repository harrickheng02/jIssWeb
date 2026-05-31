using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Common.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumReportTargetResolver
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;

    public ForumReportTargetResolver(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
    }

    public async Task<string?> ResolveTargetAuthorSubAsync(ForumReportRecord report, CancellationToken ct = default)
    {
        if (string.Equals(report.TargetType, "post", StringComparison.OrdinalIgnoreCase))
        {
            var post = await _posts.Find(x => x.Id == report.TargetId).FirstOrDefaultAsync(ct);
            if (post is null && !string.IsNullOrWhiteSpace(report.PostId))
                post = await _posts.Find(x => x.Id == report.PostId).FirstOrDefaultAsync(ct);
            return post?.AuthorSubId;
        }

        if (string.Equals(report.TargetType, "reply", StringComparison.OrdinalIgnoreCase))
        {
            var reply = await _replies.Find(x => x.Id == report.TargetId).FirstOrDefaultAsync(ct);
            return reply?.AuthorSubId;
        }

        return null;
    }
}
