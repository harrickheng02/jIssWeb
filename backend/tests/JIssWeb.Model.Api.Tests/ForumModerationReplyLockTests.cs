using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ForumModerationReplyLockTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumModerationReplyLockTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task Lock_replies_then_member_reply_returns_403()
    {
        var unlockAtEnd = async () =>
        {
            var unlock = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/replies-locked")
            {
                Content = new StringContent("{\"repliesLocked\":false}", Encoding.UTF8, "application/json"),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) },
            };
            await _fx.Client.SendAsync(unlock);
        };

        try
        {
            var lockReq = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/replies-locked")
            {
                Content = new StringContent("{\"repliesLocked\":true}", Encoding.UTF8, "application/json"),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) },
            };
            var rLock = await _fx.Client.SendAsync(lockReq);
            Assert.Equal(HttpStatusCode.OK, rLock.StatusCode);

            var replyReq = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts/me-post-a/replies")
            {
                Content = new StringContent("{\"body\":\"try\"}", Encoding.UTF8, "application/json"),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Member)) },
            };
            var rRep = await _fx.Client.SendAsync(replyReq);
            Assert.Equal(HttpStatusCode.Forbidden, rRep.StatusCode);
            var b = await rRep.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(b);
            Assert.Equal("REPLIES_LOCKED", doc.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            await unlockAtEnd();
        }
    }

    [Fact]
    public async Task Unlock_replies_allows_reply_again()
    {
        var unlock = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-b/replies-locked")
        {
            Content = new StringContent("{\"repliesLocked\":false}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) },
        };
        await _fx.Client.SendAsync(unlock);

        var replyReq = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts/me-post-b/replies")
        {
            Content = new StringContent($"{{\"body\":\"x-{Guid.NewGuid():N}\"}}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b", ForumRoleClaim.Member)) },
        };
        var rRep = await _fx.Client.SendAsync(replyReq);
        Assert.Equal(HttpStatusCode.OK, rRep.StatusCode);
    }

    [Fact]
    public async Task Audit_by_post_lists_reply_delete_under_same_thread()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        var pid = "audit-thread-" + Guid.NewGuid().ToString("N");
        var rid = "audit-reply-" + Guid.NewGuid().ToString("N");
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

        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/mod/replies/{rid}");
        delReq.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin));
        var dr = await _fx.Client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.OK, dr.StatusCode);

        var auditReq = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/mod/audit?targetType=post&targetId={pid}&page=1&pageSize=20");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin));
        var ar = await _fx.Client.SendAsync(auditReq);
        Assert.Equal(HttpStatusCode.OK, ar.StatusCode);
        var body = await ar.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        Assert.Equal(
            "删除回复",
            items[0].GetProperty("actionLabel").GetString());
    }
}
