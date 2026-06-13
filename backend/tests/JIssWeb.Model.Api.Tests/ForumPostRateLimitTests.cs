using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumAntiSpam")]
public sealed class ForumPostRateLimitTests
{
    private readonly ForumAntiSpamIntegrationFixture _fx;

    public ForumPostRateLimitTests(ForumAntiSpamIntegrationFixture fx) => _fx = fx;

    private HttpRequestMessage AuthPost(string url, string sub, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub));
        return req;
    }

    private static async Task AssertCodeAsync(HttpResponseMessage res, string code)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreatePost_EleventhRequest_Returns429()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "10");
            b.UseSetting("Forum:PostRateLimit:MaxReplies", "30");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();
        for (var i = 0; i < 11; i++)
        {
            var res = await client.SendAsync(AuthPost("/api/forum/posts", "rl-user-post",
                new { title = $"t{i}", body = $"b{i}", boardId = "general" }));
            if (i < 10)
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            else
            {
                Assert.Equal((HttpStatusCode)429, res.StatusCode);
                await AssertCodeAsync(res, "RATE_LIMITED");
            }
        }
    }

    [Fact]
    public async Task CreateReply_ThirtyFirstRequest_Returns429()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "10");
            b.UseSetting("Forum:PostRateLimit:MaxReplies", "30");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();
        for (var i = 0; i < 31; i++)
        {
            var res = await client.SendAsync(AuthPost("/api/forum/posts/aspam-post-1/replies", "rl-user-reply",
                new { body = $"reply {i}" }));
            if (i < 30)
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            else
            {
                Assert.Equal((HttpStatusCode)429, res.StatusCode);
                await AssertCodeAsync(res, "RATE_LIMITED");
            }
        }
    }

    [Fact]
    public async Task PostLimitReached_RepliesStillAllowed()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "2");
            b.UseSetting("Forum:PostRateLimit:MaxReplies", "30");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var res = await client.SendAsync(AuthPost("/api/forum/posts", "rl-mixed",
                new { title = $"t{i}", body = $"b{i}", boardId = "general" }));
            if (i < 2)
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            else
                Assert.Equal((HttpStatusCode)429, res.StatusCode);
        }

        var replyRes = await client.SendAsync(AuthPost("/api/forum/posts/aspam-post-1/replies", "rl-mixed",
            new { body = "still ok" }));
        Assert.Equal(HttpStatusCode.OK, replyRes.StatusCode);
    }

    [Fact]
    public async Task DraftPublish_NotRateLimited()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "1");
            b.UseSetting("Forum:PostRateLimit:MaxReplies", "1");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();

        var firstPost = await client.SendAsync(AuthPost("/api/forum/posts", "rl-draft",
            new { title = "first", body = "x", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, firstPost.StatusCode);

        var secondPost = await client.SendAsync(AuthPost("/api/forum/posts", "rl-draft",
            new { title = "blocked post", body = "x", boardId = "general" }));
        Assert.Equal((HttpStatusCode)429, secondPost.StatusCode);

        var createDraft = AuthPost("/api/forum/posts/drafts", "rl-draft", new { title = "d", body = "b", boardId = "general" });
        var draftRes = await client.SendAsync(createDraft);
        draftRes.EnsureSuccessStatusCode();
        var draftId = JsonDocument.Parse(await draftRes.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString();

        var pubRes = await client.SendAsync(AuthPost($"/api/forum/posts/drafts/{draftId}/publish", "rl-draft"));
        Assert.NotEqual((HttpStatusCode)429, pubRes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pubRes.StatusCode);

        using var scope = factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        await posts.DeleteManyAsync(x => x.AuthorSubId == "rl-draft");
    }
}
