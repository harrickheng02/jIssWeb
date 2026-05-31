using System.Net;
using System.Net.Http.Headers;
using System.Text;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ModerationDeleteTests
{
    private readonly ForumMeIntegrationFixture _fx;

    public ModerationDeleteTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task Admin_delete_post_removes_post()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var pid = "mod-del-post-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "x",
            Body = "y",
            Excerpt = "y",
            AuthorSubId = "user-a",
            Board = "综合",
            CreatedAtUtc = DateTime.UtcNow,
        });

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/mod/posts/{pid}");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var left = await posts.Find(x => x.Id == pid).FirstOrDefaultAsync();
        Assert.NotNull(left);
        Assert.Equal("deleted", left!.State);
    }

    [Fact]
    public async Task Moderator_without_roster_entry_can_delete_post_in_any_board()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var pid = "mod-del-rosterless-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "x",
            Body = "y",
            Excerpt = "y",
            AuthorSubId = "user-a",
            Board = "技术",
            State = "published",
            CreatedAtUtc = DateTime.UtcNow,
        });

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/mod/posts/{pid}");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-rosterless-mod", ForumRoleClaim.Moderator));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_can_delete_others_post_via_forum_posts_endpoint()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var pid = "mod-del-forum-api-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "x",
            Body = "y",
            Excerpt = "y",
            AuthorSubId = "user-a",
            Board = "综合",
            State = "published",
            CreatedAtUtc = DateTime.UtcNow,
        });

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/forum/posts/{pid}");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_can_delete_own_post_out_of_scope_via_mod_endpoint()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var pid = "mod-del-own-tech-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "x",
            Body = "y",
            Excerpt = "y",
            AuthorSubId = "user-mod",
            Board = "技术",
            State = "published",
            CreatedAtUtc = DateTime.UtcNow,
        });

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/mod/posts/{pid}");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var left = await posts.Find(x => x.Id == pid).FirstOrDefaultAsync();
        Assert.NotNull(left);
        Assert.Equal("deleted", left!.State);
        Assert.Equal("user-mod", left.DeletedBySub);
    }

    [Fact]
    public async Task Moderator_cannot_delete_post_out_of_scope()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/mod/posts/me-post-tech");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_delete_reply_in_scope()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        var pid = "mod-del-thread-" + Guid.NewGuid().ToString("N");
        var rid = "mod-del-reply-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "t",
            Body = "b",
            Excerpt = "b",
            AuthorSubId = "user-a",
            Board = "综合",
            CommentCount = 1,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await replies.InsertOneAsync(new ForumReplyRecord
        {
            Id = rid,
            PostId = pid,
            AuthorSubId = "user-b",
            Body = "r",
            CreatedAtUtc = DateTime.UtcNow,
        });

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/mod/replies/{rid}");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var replyLeft = await replies.Find(x => x.Id == rid).FirstOrDefaultAsync();
        Assert.NotNull(replyLeft);
        Assert.Equal("deleted", replyLeft!.State);
        var post = await posts.Find(x => x.Id == pid).FirstOrDefaultAsync();
        Assert.NotNull(post);
        Assert.Equal(0, post!.CommentCount);
    }

    [Fact]
    public async Task Report_queue_delete_reply_without_reason_succeeds()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        var reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var pid = "mod-del-thread-rpt-" + Guid.NewGuid().ToString("N");
        var rid = "mod-del-reply-rpt-" + Guid.NewGuid().ToString("N");
        var reportId = "mod-del-report-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "t",
            Body = "b",
            Excerpt = "b",
            AuthorSubId = "user-a",
            Board = "综合",
            CommentCount = 1,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await replies.InsertOneAsync(new ForumReplyRecord
        {
            Id = rid,
            PostId = pid,
            AuthorSubId = "user-b",
            Body = "r",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await reports.InsertOneAsync(new ForumReportRecord
        {
            Id = reportId,
            ReporterSub = "user-c",
            TargetType = "reply",
            TargetId = rid,
            PostId = pid,
            BoardId = "general",
            BoardTitle = "综合",
            Status = ForumReportStatuses.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/mod/replies/{rid}")
        {
            Content = new StringContent($"{{\"reportId\":\"{reportId}\"}}", Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" }));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        var row = await audit.Find(x => x.Action == "reply.modDelete" && x.TargetId == rid).FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.True(row!.Metadata!.ContainsKey("reportId"));
        Assert.False(row.Metadata.ContainsKey("reason"));
    }
}
