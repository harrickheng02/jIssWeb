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
public sealed class ForumDraftTests
{
    private readonly ForumMeIntegrationFixture _fx;
    public ForumDraftTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private HttpRequestMessage AuthRequest(HttpMethod method, string url, string sub, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub));
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    [Fact]
    public async Task Create_draft_returns_200_with_draft_id()
    {
        var r = await _fx.Client.SendAsync(AuthRequest(HttpMethod.Post, "/api/forum/posts/drafts", "user-a",
            new { title = "draft title", body = "draft body" }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        var id = json["data"]!["id"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal("draft", json["data"]!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task Draft_not_in_public_post_list()
    {
        // Create a draft
        var cr = await _fx.Client.SendAsync(AuthRequest(HttpMethod.Post, "/api/forum/posts/drafts", "user-a",
            new { title = "invisible draft", body = "b" }));
        var json = JsonNode.Parse(await cr.Content.ReadAsStringAsync())!;
        var id = json["data"]!["id"]!.GetValue<string>();

        // Public list should not include it
        var lr = await _fx.Client.GetAsync("/api/forum/posts?page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, lr.StatusCode);
        Assert.DoesNotContain(id, await lr.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_draft_returns_200()
    {
        var cr = await _fx.Client.SendAsync(AuthRequest(HttpMethod.Post, "/api/forum/posts/drafts", "user-a",
            new { title = "t", body = "b" }));
        var id = JsonNode.Parse(await cr.Content.ReadAsStringAsync())!["data"]!["id"]!.GetValue<string>();

        var ur = await _fx.Client.SendAsync(AuthRequest(HttpMethod.Put, $"/api/forum/posts/drafts/{id}", "user-a",
            new { title = "updated title" }));
        Assert.Equal(HttpStatusCode.OK, ur.StatusCode);
        var json = JsonNode.Parse(await ur.Content.ReadAsStringAsync())!;
        Assert.Equal("updated title", json["data"]!["title"]!.GetValue<string>());
        Assert.Equal("draft", json["data"]!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task Delete_draft_returns_200_and_removes_draft()
    {
        var cr = await _fx.Client.SendAsync(AuthRequest(HttpMethod.Post, "/api/forum/posts/drafts", "user-a",
            new { title = "to delete", body = "b" }));
        var id = JsonNode.Parse(await cr.Content.ReadAsStringAsync())!["data"]!["id"]!.GetValue<string>();

        var dr = await _fx.Client.SendAsync(AuthRequest(HttpMethod.Delete, $"/api/forum/posts/drafts/{id}", "user-a"));
        Assert.Equal(HttpStatusCode.OK, dr.StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var left = await posts.Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.Null(left);
    }

    [Fact]
    public async Task Non_owner_cannot_delete_draft()
    {
        var cr = await _fx.Client.SendAsync(AuthRequest(HttpMethod.Post, "/api/forum/posts/drafts", "user-a",
            new { title = "t", body = "b" }));
        var id = JsonNode.Parse(await cr.Content.ReadAsStringAsync())!["data"]!["id"]!.GetValue<string>();

        var dr = await _fx.Client.SendAsync(AuthRequest(HttpMethod.Delete, $"/api/forum/posts/drafts/{id}", "user-b"));
        Assert.Equal(HttpStatusCode.Forbidden, dr.StatusCode);
    }
}
