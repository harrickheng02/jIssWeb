using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumAntiSpam")]
public sealed class ForumBlockedWordTests
{
    private readonly ForumAntiSpamIntegrationFixture _fx;

    public ForumBlockedWordTests(ForumAntiSpamIntegrationFixture fx) => _fx = fx;

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
    public async Task CreatePost_EmptyWordList_Succeeds()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
        });
        using var client = factory.CreateClient();
        var res = await client.SendAsync(AuthPost("/api/forum/posts", "user-a",
            new { title = "ok title", body = "ok body", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task CreatePost_TitleBlocked_Returns400WithoutEcho()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "badword");
        });
        using var client = factory.CreateClient();
        var res = await client.SendAsync(AuthPost("/api/forum/posts", "user-a",
            new { title = "Title BADWORD here", body = "clean", boardId = "general" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertCodeAsync(res, "BLOCKED_CONTENT");
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("badword", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePost_BodyBlocked_Returns400()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "spam");
        });
        using var client = factory.CreateClient();
        var res = await client.SendAsync(AuthPost("/api/forum/posts", "user-b",
            new { title = "fine", body = "contains SPAM token", boardId = "general" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertCodeAsync(res, "BLOCKED_CONTENT");
    }

    [Fact]
    public async Task CreateReply_BodyBlocked_Returns400()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "evil");
        });
        using var client = factory.CreateClient();
        var res = await client.SendAsync(AuthPost("/api/forum/posts/aspam-post-1/replies", "user-c",
            new { body = "Evil content" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertCodeAsync(res, "BLOCKED_CONTENT");
    }
}
