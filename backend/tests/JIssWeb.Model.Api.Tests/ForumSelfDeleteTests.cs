using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumSelfDelete")]
public sealed class ForumSelfDeleteTests : IClassFixture<ForumSelfDeleteIntegrationFixture>
{
    private readonly ForumSelfDeleteIntegrationFixture _fx;

    public ForumSelfDeleteTests(ForumSelfDeleteIntegrationFixture fx) => _fx = fx;

    // ─────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static HttpRequestMessage AuthorizedDelete(string url, string sub) =>
        new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub)) }
        };

    private static HttpRequestMessage AuthorizedGet(string url, string sub) =>
        new HttpRequestMessage(HttpMethod.Get, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub)) }
        };

    private IMongoCollection<ForumPostRecord> PostsCollection()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        return mongo.GetDatabase(_fx.DatabaseName)
                    .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
    }

    private IMongoCollection<ForumReplyRecord> RepliesCollection()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        return mongo.GetDatabase(_fx.DatabaseName)
                    .GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
    }

    private IMongoCollection<ForumPostLikeRecord> LikesCollection()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        return mongo.GetDatabase(_fx.DatabaseName)
                    .GetCollection<ForumPostLikeRecord>(ForumMongoSetup.LikesCollectionName);
    }

    private IMongoCollection<ForumPostFavoriteRecord> FavoritesCollection()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        return mongo.GetDatabase(_fx.DatabaseName)
                    .GetCollection<ForumPostFavoriteRecord>(ForumMongoSetup.FavoritesCollectionName);
    }

    private async Task<string> InsertPublishedPost(string authorSub, int likeCount = 0, int commentCount = 0)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var id = "sd-post-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = id,
            Title = "test-title",
            Body = "test-body",
            Excerpt = "test-body",
            AuthorSubId = authorSub,
            Board = "综合",
            State = "published",
            LikeCount = likeCount,
            CommentCount = commentCount,
            CreatedAtUtc = DateTime.UtcNow,
        });
        return id;
    }

    private async Task<string> InsertPublishedReply(string postId, string authorSub)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        // increment CommentCount
        await posts.UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update.Inc(x => x.CommentCount, 1));
        var id = "sd-reply-" + Guid.NewGuid().ToString("N");
        await replies.InsertOneAsync(new ForumReplyRecord
        {
            Id = id,
            PostId = postId,
            AuthorSubId = authorSub,
            Body = "reply-body",
            State = "published",
            CreatedAtUtc = DateTime.UtcNow,
        });
        return id;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. DeletePost: author soft-deletes → 200
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DeletePost_Author_SoftDeletes_Returns200()
    {
        var postId = await InsertPublishedPost("author-a");

        var req = AuthorizedDelete($"/api/forum/posts/{postId}", "author-a");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // verify soft-deleted in DB
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var post = await mongo.GetDatabase(_fx.DatabaseName)
                              .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
                              .Find(x => x.Id == postId).FirstOrDefaultAsync();
        Assert.NotNull(post);
        Assert.Equal("deleted", post!.State);
        Assert.Equal("author-a", post.DeletedBySub);
        Assert.NotNull(post.DeletedAtUtc);

        // other user GET → 404
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/forum/posts/{postId}");
        var getR = await _fx.Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.NotFound, getR.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DeletePost: non-author → 403
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DeletePost_NonAuthor_Returns403()
    {
        var postId = await InsertPublishedPost("author-b");

        var req = AuthorizedDelete($"/api/forum/posts/{postId}", "other-user");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. DeletePost: already deleted → 404
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DeletePost_AlreadyDeleted_Returns404()
    {
        var postId = await InsertPublishedPost("author-c");
        // first delete
        await _fx.Client.SendAsync(AuthorizedDelete($"/api/forum/posts/{postId}", "author-c"));
        // second delete
        var r = await _fx.Client.SendAsync(AuthorizedDelete($"/api/forum/posts/{postId}", "author-c"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. GetById: author self-deleted → author can see (200), state=deleted
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetById_AuthorSelfDeleted_AuthorCanSee_Returns200()
    {
        var postId = await InsertPublishedPost("author-d");
        // soft delete as author
        await _fx.Client.SendAsync(AuthorizedDelete($"/api/forum/posts/{postId}", "author-d"));

        // author GET → 200, state=deleted
        var req = AuthorizedGet($"/api/forum/posts/{postId}", "author-d");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("deleted", data.GetProperty("state").GetString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. GetById: author self-deleted → other user → 404
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetById_AuthorSelfDeleted_OtherUser_Returns404()
    {
        var postId = await InsertPublishedPost("author-e");
        await _fx.Client.SendAsync(AuthorizedDelete($"/api/forum/posts/{postId}", "author-e"));

        var req = AuthorizedGet($"/api/forum/posts/{postId}", "other-user-e");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. DeleteReply: author soft-deletes own reply → 200
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DeleteReply_Author_SoftDeletes_Returns200()
    {
        var postId = await InsertPublishedPost("post-author");
        var replyId = await InsertPublishedReply(postId, "reply-author");

        var req = AuthorizedDelete($"/api/forum/posts/{postId}/replies/{replyId}", "reply-author");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // verify reply soft-deleted
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var reply = await db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName)
                            .Find(x => x.Id == replyId).FirstOrDefaultAsync();
        Assert.NotNull(reply);
        Assert.Equal("deleted", reply!.State);
        Assert.Equal("reply-author", reply.DeletedBySub);

        // CommentCount decremented
        var post = await db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
                           .Find(x => x.Id == postId).FirstOrDefaultAsync();
        Assert.NotNull(post);
        Assert.Equal(0, post!.CommentCount);

        // reply no longer in public replies list
        var listResp = await _fx.Client.GetAsync($"/api/forum/posts/{postId}/replies");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listBody = await listResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(replyId, listBody);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. DeleteReply: non-author → 403
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DeleteReply_NonAuthor_Returns403()
    {
        var postId = await InsertPublishedPost("post-author2");
        var replyId = await InsertPublishedReply(postId, "reply-author2");

        var req = AuthorizedDelete($"/api/forum/posts/{postId}/replies/{replyId}", "other-user2");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. PermanentDelete: zero engagement → 200, then GET → 404
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PermanentDelete_ZeroEngagement_Returns200()
    {
        var postId = await InsertPublishedPost("perm-author");
        // soft delete first
        await _fx.Client.SendAsync(AuthorizedDelete($"/api/forum/posts/{postId}", "perm-author"));

        // permanent delete
        var req = AuthorizedDelete($"/api/forum/posts/{postId}/permanent", "perm-author");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // verify physically deleted
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var post = await mongo.GetDatabase(_fx.DatabaseName)
                              .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
                              .Find(x => x.Id == postId).FirstOrDefaultAsync();
        Assert.Null(post);

        // GET → 404
        var getResp = await _fx.Client.GetAsync($"/api/forum/posts/{postId}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. PermanentDelete: has likes → 400 HAS_ENGAGEMENT
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PermanentDelete_WithEngagement_Returns400()
    {
        var postId = await InsertPublishedPost("perm-author2", likeCount: 1);
        // soft delete first (by directly updating DB since author self-delete requires published state)
        using var scope2 = _fx.Factory.Services.CreateScope();
        var mongo2 = scope2.ServiceProvider.GetRequiredService<IMongoClient>();
        await mongo2.GetDatabase(_fx.DatabaseName)
                    .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
                    .UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update
                        .Set(x => x.State, "deleted")
                        .Set(x => x.DeletedAtUtc, DateTime.UtcNow)
                        .Set(x => x.DeletedBySub, "perm-author2"));

        var req = AuthorizedDelete($"/api/forum/posts/{postId}/permanent", "perm-author2");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("HAS_ENGAGEMENT", body);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. PermanentDelete: moderator-deleted → author cannot permanent delete → 403
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PermanentDelete_ModeratorDeleted_Returns403()
    {
        var postId = await InsertPublishedPost("mod-victim");
        // simulate moderator soft delete: DeletedBySub != AuthorSubId
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        await mongo.GetDatabase(_fx.DatabaseName)
                   .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
                   .UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update
                       .Set(x => x.State, "deleted")
                       .Set(x => x.DeletedAtUtc, DateTime.UtcNow)
                       .Set(x => x.DeletedBySub, "user-mod"));  // mod deleted it

        var req = AuthorizedDelete($"/api/forum/posts/{postId}/permanent", "mod-victim");
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
