using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, $"/api/forum/posts/drafts/{id}/publish", "user-a"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.Equal("published", json["data"]!["state"]!.GetValue<string>());

        // Now visible in public list
        var lr = await _fx.Client.GetAsync("/api/forum/posts?page=1&pageSize=50");
        Assert.Contains(id, await lr.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Publish_draft_missing_title_returns_400()
    {
        var cr = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, "/api/forum/posts/drafts", "user-a",
            new { body = "body only", boardId = "general" }));
        var id = JsonNode.Parse(await cr.Content.ReadAsStringAsync())!["data"]!["id"]!.GetValue<string>();

        var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, $"/api/forum/posts/drafts/{id}/publish", "user-a"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Non_owner_publish_returns_403()
    {
        var id = await CreateDraftAsync("user-a", "t", "b");
        var r = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, $"/api/forum/posts/drafts/{id}/publish", "user-b"));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
