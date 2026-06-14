using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumAntiSpam")]
public sealed class ForumAntiSpamReviewFixTests
{
    private readonly ForumAntiSpamIntegrationFixture _fx;

    public ForumAntiSpamReviewFixTests(ForumAntiSpamIntegrationFixture fx) => _fx = fx;

    private HttpRequestMessage Auth(HttpMethod method, string url, string sub, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
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
    public async Task AtCap_WithBlockedWord_Returns429BeforeBlockedContent()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "1");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "evil");
        });
        using var client = factory.CreateClient();

        var first = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-priority",
            new { title = "ok", body = "ok", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-priority",
            new { title = "ok2", body = "ok", boardId = "general" }));
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
        await AssertCodeAsync(second, "RATE_LIMITED");
    }

    [Fact]
    public async Task PutSelfEdit_WithBlockedWord_Returns400()
    {
        // change-A: PUT self-edit is now covered by blocked-word filter
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "evil");
        });
        using var client = factory.CreateClient();

        var res = await client.SendAsync(Auth(HttpMethod.Put, "/api/forum/posts/aspam-post-1", "user-author",
            new { title = "evil title", body = "updated body" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertCodeAsync(res, "BLOCKED_CONTENT");
    }

    [Fact]
    public async Task InvalidInput_DoesNotConsumePostQuota()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "1");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();

        var ok = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-invalid",
            new { title = "only one", body = "ok", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        for (var i = 0; i < 5; i++)
        {
            var bad = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-invalid",
                new { title = " ", body = "x", boardId = "general" }));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            await AssertCodeAsync(bad, "INVALID_INPUT");
        }

        var secondOk = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-invalid",
            new { title = "second ok", body = "ok", boardId = "general" }));
        Assert.Equal((HttpStatusCode)429, secondOk.StatusCode);
    }

    [Fact]
    public async Task MutedUser_DoesNotConsumePostQuota()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "1");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();

        var first = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-muted",
            new { title = "first", body = "ok", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        _fx.Sanctions.SetMuted("rl-muted");
        var muted = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-muted",
            new { title = "blocked", body = "x", boardId = "general" }));
        Assert.Equal(HttpStatusCode.Forbidden, muted.StatusCode);
        await AssertCodeAsync(muted, "FORUM_MUTED");

        _fx.Sanctions.ClearMute("rl-muted");
        var second = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-muted",
            new { title = "second", body = "ok", boardId = "general" }));
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
    }

    [Fact]
    public async Task SearchRateLimit_DoesNotAffectPostCreateQuota()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:SearchRateLimit:MaxRequests", "1");
            b.UseSetting("Forum:SearchRateLimit:WindowSeconds", "60");
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "2");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();

        var s1 = await client.GetAsync("/api/forum/posts?q=spam-test");
        Assert.Equal(HttpStatusCode.OK, s1.StatusCode);
        var s2 = await client.GetAsync("/api/forum/posts?q=spam-test");
        Assert.Equal((HttpStatusCode)429, s2.StatusCode);

        for (var i = 0; i < 2; i++)
        {
            var post = await client.SendAsync(Auth(HttpMethod.Post, "/api/forum/posts", "rl-search-independent",
                new { title = $"p{i}", body = "b", boardId = "general" }));
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        }
    }
}
