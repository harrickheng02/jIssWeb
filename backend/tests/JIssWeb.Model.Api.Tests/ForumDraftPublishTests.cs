using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ForumDraftPublishTests
{
    private readonly ForumMeIntegrationFixture _fx;
    public ForumDraftPublishTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private HttpRequestMessage AuthReq(HttpMethod m, string url, string sub, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub));
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    private async Task<string> CreateDraftAsync(string sub, string title, string body, string boardId = "general")
    {
        var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, "/api/forum/posts/drafts", sub,
            new { title, body, boardId }));
        r.EnsureSuccessStatusCode();
        return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["data"]!["id"]!.GetValue<string>();
    }

    [Fact]
    public async Task Publish_valid_draft_returns_200_and_appears_in_list()
    {
        var id = await CreateDraftAsync("user-a", "published title", "published body");
        try
        {
            var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, $"/api/forum/posts/drafts/{id}/publish", "user-a"));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
            Assert.Equal("published", json["data"]!["state"]!.GetValue<string>());

            var lr = await _fx.Client.GetAsync("/api/forum/posts?page=1&pageSize=50");
            Assert.Contains(id, await lr.Content.ReadAsStringAsync());
        }
        finally
        {
            using var scope = _fx.Factory.Services.CreateScope();
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
            await posts.DeleteOneAsync(x => x.Id == id);
        }
    }

    [Fact]
    public async Task Publish_draft_missing_title_returns_400()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var id = "draft-no-title-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = id,
            Title = "",
            Body = "body only",
            Excerpt = "body only",
            AuthorSubId = "user-a",
            Board = "综合",
            State = "draft",
            CreatedAtUtc = DateTime.UtcNow,
        });

        var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, $"/api/forum/posts/drafts/{id}/publish", "user-a"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Non_owner_publish_returns_403()
    {
        var id = await CreateDraftAsync("user-a", "t", "b");
        try
        {
            var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, $"/api/forum/posts/drafts/{id}/publish", "user-b"));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally
        {
            using var scope = _fx.Factory.Services.CreateScope();
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
            await posts.DeleteOneAsync(x => x.Id == id);
        }
    }

    [Fact]
    public async Task Publish_draft_with_invalid_board_returns_400()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var id = "draft-bad-board-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = id,
            Title = "t",
            Body = "b",
            Excerpt = "b",
            AuthorSubId = "user-a",
            Board = "不存在的板块",
            State = "draft",
            CreatedAtUtc = DateTime.UtcNow,
        });

        var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, $"/api/forum/posts/drafts/{id}/publish", "user-a"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var json = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.Equal("INVALID_BOARD_ID", json["code"]!.GetValue<string>());
    }
}
