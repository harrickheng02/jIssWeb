using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ForumMeDraftsTests
{
    private readonly ForumMeIntegrationFixture _fx;
    public ForumMeDraftsTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private HttpRequestMessage AuthReq(HttpMethod m, string url, string sub, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub));
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    [Fact]
    public async Task MyDrafts_returns_only_own_drafts()
    {
        // Create 2 drafts for user-a, 1 for user-b
        await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, "/api/forum/posts/drafts", "user-a", new { title = "da1", body = "b" }));
        await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, "/api/forum/posts/drafts", "user-a", new { title = "da2", body = "b" }));
        await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, "/api/forum/posts/drafts", "user-b", new { title = "db1", body = "b" }));

        var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Get, "/api/forum/me/drafts?page=1&pageSize=20", "user-a"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        var items = json["data"]!["items"]!.AsArray();
        // All items should belong to user-a
        foreach (var item in items)
            Assert.Equal("user-a", item!["authorId"]!.GetValue<string>());
    }

    [Fact]
    public async Task MyDrafts_unauthenticated_returns_401()
    {
        var r = await _fx.Client.GetAsync("/api/forum/me/drafts");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
