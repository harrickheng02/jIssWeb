using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ForumReplySelfEditTests
{
    private readonly ForumMeIntegrationFixture _fx;
    public ForumReplySelfEditTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private async Task<(string postId, string replyId)> SeedPostAndReplyAsync(
        string postAuthorSub,
        string replyAuthorSub,
        bool repliesLocked = false,
        string postState = "published",
        string replyState = "published")
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        var pid = "reply-edit-post-" + Guid.NewGuid().ToString("N");
        var rid = "reply-edit-reply-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid, Title = "t", Body = "b", Excerpt = "b",
            AuthorSubId = postAuthorSub, Board = "综合",
            State = postState, RepliesLocked = repliesLocked,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await replies.InsertOneAsync(new ForumReplyRecord
        {
            Id = rid, PostId = pid, AuthorSubId = replyAuthorSub,
            Body = "original reply", State = replyState,
            CreatedAtUtc = DateTime.UtcNow,
        });
        return (pid, rid);
    }

    private async Task DeletePostAndRepliesAsync(string postId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        await db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName)
            .DeleteManyAsync(x => x.PostId == postId);
        await db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
            .DeleteOneAsync(x => x.Id == postId);
    }

    [Fact]
    public async Task Author_edit_reply_returns_200_and_sets_UpdatedAtUtc()
    {
        var (pid, rid) = await SeedPostAndReplyAsync("user-a", "user-b");
        try
        {
            var body = JsonSerializer.Serialize(new { body = "edited reply" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}/replies/{rid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            using var scope = _fx.Factory.Services.CreateScope();
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var replies = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
            var updated = await replies.Find(x => x.Id == rid).FirstOrDefaultAsync();
            Assert.Equal("edited reply", updated!.Body);
            Assert.NotNull(updated.UpdatedAtUtc);
        }
        finally
        {
            await DeletePostAndRepliesAsync(pid);
        }
    }

    [Fact]
    public async Task Non_author_edit_reply_returns_403()
    {
        var (pid, rid) = await SeedPostAndReplyAsync("user-a", "user-b");
        try
        {
            var body = JsonSerializer.Serialize(new { body = "hacked" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}/replies/{rid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally
        {
            await DeletePostAndRepliesAsync(pid);
        }
    }

    [Fact]
    public async Task Edit_reply_on_locked_post_returns_200()
    {
        var (pid, rid) = await SeedPostAndReplyAsync("user-a", "user-b", repliesLocked: true);
        try
        {
            var body = JsonSerializer.Serialize(new { body = "edit on locked post" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}/replies/{rid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        finally
        {
            await DeletePostAndRepliesAsync(pid);
        }
    }

    [Fact]
    public async Task Edit_deleted_reply_returns_404()
    {
        var (pid, rid) = await SeedPostAndReplyAsync("user-a", "user-b", replyState: "deleted");
        try
        {
            var body = JsonSerializer.Serialize(new { body = "edit deleted" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}/replies/{rid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        finally
        {
            await DeletePostAndRepliesAsync(pid);
        }
    }

    [Fact]
    public async Task Edit_reply_on_deleted_post_returns_404()
    {
        var (pid, rid) = await SeedPostAndReplyAsync("user-a", "user-b", postState: "deleted");
        try
        {
            var body = JsonSerializer.Serialize(new { body = "edit on deleted post" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}/replies/{rid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        finally
        {
            await DeletePostAndRepliesAsync(pid);
        }
    }
}
