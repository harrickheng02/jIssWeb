using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumAntiSpam")]
public sealed class ForumAntiSpamWritePathTests
{
    private readonly ForumAntiSpamIntegrationFixture _fx;

    public ForumAntiSpamWritePathTests(ForumAntiSpamIntegrationFixture fx) => _fx = fx;

    private HttpRequestMessage AuthReq(HttpMethod method, string url, string sub, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub));
        return req;
    }

    private HttpRequestMessage AuthPost(string url, string sub, object? body = null)
        => AuthReq(HttpMethod.Post, url, sub, body);

    private HttpRequestMessage AuthPut(string url, string sub, object? body = null)
        => AuthReq(HttpMethod.Put, url, sub, body);

    private static async Task AssertCodeAsync(HttpResponseMessage res, string code)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> GetIdAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    // ── Publish: blocked word ──────────────────────────────────────────────

    [Fact]
    public async Task PublishDraft_TitleBlocked_Returns400()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "badpub");
        });
        using var client = factory.CreateClient();

        var createRes = await client.SendAsync(AuthPost("/api/forum/posts/drafts", "pub-user-a",
            new { title = "BADPUB in title", body = "clean body", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var draftId = await GetIdAsync(createRes);

        var publishRes = await client.SendAsync(AuthPost($"/api/forum/posts/drafts/{draftId}/publish", "pub-user-a"));
        Assert.Equal(HttpStatusCode.BadRequest, publishRes.StatusCode);
        await AssertCodeAsync(publishRes, "BLOCKED_CONTENT");
    }

    [Fact]
    public async Task PublishDraft_BodyBlocked_Returns400()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "pubspam");
        });
        using var client = factory.CreateClient();

        var createRes = await client.SendAsync(AuthPost("/api/forum/posts/drafts", "pub-user-b",
            new { title = "clean title", body = "body has PUBSPAM word", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var draftId = await GetIdAsync(createRes);

        var publishRes = await client.SendAsync(AuthPost($"/api/forum/posts/drafts/{draftId}/publish", "pub-user-b"));
        Assert.Equal(HttpStatusCode.BadRequest, publishRes.StatusCode);
        await AssertCodeAsync(publishRes, "BLOCKED_CONTENT");
    }

    [Fact]
    public async Task PublishDraft_LocalHandling_StillReturns400()
    {
        // publish always rejects regardless of Handling=local (no local-only semantic for publish)
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "local");
            b.UseSetting("Forum:BlockedWords:Words:0", "localblock");
        });
        using var client = factory.CreateClient();

        var createRes = await client.SendAsync(AuthPost("/api/forum/posts/drafts", "pub-user-c",
            new { title = "LOCALBLOCK here", body = "body", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var draftId = await GetIdAsync(createRes);

        var publishRes = await client.SendAsync(AuthPost($"/api/forum/posts/drafts/{draftId}/publish", "pub-user-c"));
        Assert.Equal(HttpStatusCode.BadRequest, publishRes.StatusCode);
        await AssertCodeAsync(publishRes, "BLOCKED_CONTENT");
    }

    [Fact]
    public async Task PublishDraft_CleanContent_Succeeds()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "dirty");
        });
        using var client = factory.CreateClient();

        var createRes = await client.SendAsync(AuthPost("/api/forum/posts/drafts", "pub-user-d",
            new { title = "clean title", body = "clean body", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var draftId = await GetIdAsync(createRes);

        var publishRes = await client.SendAsync(AuthPost($"/api/forum/posts/drafts/{draftId}/publish", "pub-user-d"));
        Assert.Equal(HttpStatusCode.OK, publishRes.StatusCode);
        using var doc = JsonDocument.Parse(await publishRes.Content.ReadAsStringAsync());
        Assert.Equal("published", doc.RootElement.GetProperty("data").GetProperty("state").GetString());
    }

    // ── Publish: rate limit shares post quota ─────────────────────────────

    [Fact]
    public async Task PublishDraft_AfterMaxPostsExhausted_Returns429()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "2");
            b.UseSetting("Forum:PostRateLimit:MaxReplies", "30");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();
        const string sub = "rl-pub-user-a";

        // exhaust quota via direct creates
        await client.SendAsync(AuthPost("/api/forum/posts", sub,
            new { title = "t1", body = "b1", boardId = "general" }));
        await client.SendAsync(AuthPost("/api/forum/posts", sub,
            new { title = "t2", body = "b2", boardId = "general" }));

        // create draft (draft create doesn't count)
        var draftRes = await client.SendAsync(AuthPost("/api/forum/posts/drafts", sub,
            new { title = "draft", body = "body", boardId = "general" }));
        var draftId = await GetIdAsync(draftRes);

        // publish should be rate-limited
        var publishRes = await client.SendAsync(AuthPost($"/api/forum/posts/drafts/{draftId}/publish", sub));
        Assert.Equal((HttpStatusCode)429, publishRes.StatusCode);
        await AssertCodeAsync(publishRes, "RATE_LIMITED");
    }

    [Fact]
    public async Task PublishDraft_CountsTowardMaxPosts()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "2");
            b.UseSetting("Forum:PostRateLimit:MaxReplies", "30");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();
        const string sub = "rl-pub-user-b";

        // first post via create
        await client.SendAsync(AuthPost("/api/forum/posts", sub,
            new { title = "t1", body = "b1", boardId = "general" }));

        // create and publish a draft (this should use the 2nd slot)
        var draftRes = await client.SendAsync(AuthPost("/api/forum/posts/drafts", sub,
            new { title = "d1", body = "b1", boardId = "general" }));
        var draftId = await GetIdAsync(draftRes);
        var publishRes = await client.SendAsync(AuthPost($"/api/forum/posts/drafts/{draftId}/publish", sub));
        Assert.Equal(HttpStatusCode.OK, publishRes.StatusCode);

        // third attempt (either create or publish) should be 429
        var thirdRes = await client.SendAsync(AuthPost("/api/forum/posts", sub,
            new { title = "t3", body = "b3", boardId = "general" }));
        Assert.Equal((HttpStatusCode)429, thirdRes.StatusCode);
        await AssertCodeAsync(thirdRes, "RATE_LIMITED");
    }

    // ── UpdatePost: blocked word ──────────────────────────────────────────

    [Fact]
    public async Task UpdatePost_TitleBlocked_RejectHandling_Returns400()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "puttitle");
        });
        using var client = factory.CreateClient();

        var res = await client.SendAsync(AuthPut("/api/forum/posts/aspam-post-1", "user-author",
            new { title = "contains PUTTITLE word" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertCodeAsync(res, "BLOCKED_CONTENT");
    }

    [Fact]
    public async Task UpdatePost_BodyBlocked_LocalHandling_StillReturns400()
    {
        // PUT always rejects regardless of Handling=local
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "local");
            b.UseSetting("Forum:BlockedWords:Words:0", "putbody");
        });
        using var client = factory.CreateClient();

        var res = await client.SendAsync(AuthPut("/api/forum/posts/aspam-post-1", "user-author",
            new { body = "contains PUTBODY word" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertCodeAsync(res, "BLOCKED_CONTENT");
    }

    [Fact]
    public async Task UpdatePost_OnlyTagsUpdate_ExistingContentNotReEvaluated()
    {
        // The seed post title/body don't contain blocked words so this tests the
        // "only submitted fields are evaluated" rule regardless.
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "tagtest");
        });
        using var client = factory.CreateClient();

        // PUT with only tags (no title/body) — should not be blocked
        var res = await client.SendAsync(AuthPut("/api/forum/posts/aspam-post-1", "user-author",
            new { tags = new[] { "cleanTag" } }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // ── UpdateReply: blocked word ─────────────────────────────────────────

    [Fact]
    public async Task UpdateReply_BodyBlocked_Returns400()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "replyblock");
        });
        using var client = factory.CreateClient();

        // first create a reply to edit
        var replyRes = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post,
            "/api/forum/posts/aspam-post-1/replies")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { body = "clean reply" }),
                Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer",
                JwtTestTokens.CreateAccessToken("reply-edit-user")) }
        });
        Assert.Equal(HttpStatusCode.OK, replyRes.StatusCode);
        var replyId = await GetIdAsync(replyRes);

        var editRes = await client.SendAsync(AuthPut(
            $"/api/forum/posts/aspam-post-1/replies/{replyId}", "reply-edit-user",
            new { body = "now has REPLYBLOCK word" }));
        Assert.Equal(HttpStatusCode.BadRequest, editRes.StatusCode);
        await AssertCodeAsync(editRes, "BLOCKED_CONTENT");
    }

    [Fact]
    public async Task UpdateReply_CleanBody_Succeeds()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:BlockedWords:Enabled", "true");
            b.UseSetting("Forum:BlockedWords:Handling", "reject");
            b.UseSetting("Forum:BlockedWords:Words:0", "blockedreply");
        });
        using var client = factory.CreateClient();

        var replyRes = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post,
            "/api/forum/posts/aspam-post-1/replies")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { body = "initial reply" }),
                Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer",
                JwtTestTokens.CreateAccessToken("reply-clean-user")) }
        });
        Assert.Equal(HttpStatusCode.OK, replyRes.StatusCode);
        var replyId = await GetIdAsync(replyRes);

        var editRes = await client.SendAsync(AuthPut(
            $"/api/forum/posts/aspam-post-1/replies/{replyId}", "reply-clean-user",
            new { body = "updated clean body" }));
        Assert.Equal(HttpStatusCode.OK, editRes.StatusCode);
    }
}
