using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumAntiSpam")]
public sealed class AgentBlockedWordExemptionTests
{
    private readonly ForumAntiSpamIntegrationFixture _fx;

    public AgentBlockedWordExemptionTests(ForumAntiSpamIntegrationFixture fx) => _fx = fx;

    private static void ConfigureRejectWord(IWebHostBuilder builder)
    {
        builder.UseSetting("Forum:BlockedWords:Enabled", "true");
        builder.UseSetting("Forum:BlockedWords:Handling", "reject");
        builder.UseSetting("Forum:BlockedWords:Words:0", "arena-block");
    }

    private static HttpRequestMessage AuthPost(string url, string sub, object? body = null, string? accountType = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken(sub, accountType: accountType));
        return req;
    }

    private static async Task<JsonElement> ReadRootAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<IMongoCollection<ForumPostRecord>> PostsAsync(WebApplicationFactory<Program> factory)
    {
        await Task.CompletedTask;
        var mongo = factory.Services.GetRequiredService<IMongoClient>();
        var dbName = factory.Services.GetRequiredService<IOptions<MongoSettings>>().Value.DatabaseName;
        return mongo.GetDatabase(dbName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
    }

    private static async Task<IMongoCollection<ForumReplyRecord>> RepliesAsync(WebApplicationFactory<Program> factory)
    {
        await Task.CompletedTask;
        var mongo = factory.Services.GetRequiredService<IMongoClient>();
        var dbName = factory.Services.GetRequiredService<IOptions<MongoSettings>>().Value.DatabaseName;
        return mongo.GetDatabase(dbName).GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
    }

    [Fact]
    public async Task Agent_create_post_with_blocked_word_succeeds_and_persists()
    {
        using var factory = _fx.CreateFactory(ConfigureRejectWord);
        using var client = factory.CreateClient();

        var res = await client.SendAsync(AuthPost(
            "/api/forum/posts",
            "agent-blocked-post",
            new { title = "clean title", body = "body has ARENA-BLOCK token", boardId = "general" },
            accountType: "agent"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var root = await ReadRootAsync(res);
        var postId = root.GetProperty("data").GetProperty("id").GetString()!;
        var persisted = await (await PostsAsync(factory)).Find(x => x.Id == postId).FirstOrDefaultAsync();
        Assert.NotNull(persisted);
        Assert.Contains("ARENA-BLOCK", persisted!.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Human_create_post_with_blocked_word_still_returns_400()
    {
        using var factory = _fx.CreateFactory(ConfigureRejectWord);
        using var client = factory.CreateClient();

        var res = await client.SendAsync(AuthPost(
            "/api/forum/posts",
            "human-blocked-post",
            new { title = "clean title", body = "body has ARENA-BLOCK token", boardId = "general" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var root = await ReadRootAsync(res);
        Assert.Equal("BLOCKED_CONTENT", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Agent_create_reply_with_blocked_word_succeeds_and_persists()
    {
        using var factory = _fx.CreateFactory(ConfigureRejectWord);
        using var client = factory.CreateClient();

        var res = await client.SendAsync(AuthPost(
            "/api/forum/posts/aspam-post-1/replies",
            "agent-blocked-reply",
            new { body = "reply has ARENA-BLOCK token" },
            accountType: "agent"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var root = await ReadRootAsync(res);
        var replyId = root.GetProperty("data").GetProperty("id").GetString()!;
        var persisted = await (await RepliesAsync(factory)).Find(x => x.Id == replyId).FirstOrDefaultAsync();
        Assert.NotNull(persisted);
        Assert.Contains("ARENA-BLOCK", persisted!.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Agent_publish_draft_with_blocked_word_succeeds()
    {
        using var factory = _fx.CreateFactory(ConfigureRejectWord);
        using var client = factory.CreateClient();

        var create = await client.SendAsync(AuthPost(
            "/api/forum/posts/drafts",
            "agent-blocked-draft",
            new { title = "clean title", body = "draft has ARENA-BLOCK token", boardId = "general" },
            accountType: "agent"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var draftId = (await ReadRootAsync(create)).GetProperty("data").GetProperty("id").GetString()!;

        var publish = await client.SendAsync(AuthPost(
            $"/api/forum/posts/drafts/{draftId}/publish",
            "agent-blocked-draft",
            accountType: "agent"));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var persisted = await (await PostsAsync(factory)).Find(x => x.Id == draftId).FirstOrDefaultAsync();
        Assert.Equal("published", persisted!.State);
    }

    [Fact]
    public async Task Invalid_accountType_token_returns_401()
    {
        using var factory = _fx.CreateFactory();
        using var client = factory.CreateClient();
        var res = await client.SendAsync(AuthPost(
            "/api/forum/posts",
            "bad-account-type",
            new { title = "t", body = "b", boardId = "general" },
            accountType: "robot"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Agent_self_edit_post_with_blocked_word_succeeds()
    {
        using var factory = _fx.CreateFactory(ConfigureRejectWord);
        using var client = factory.CreateClient();
        const string sub = "agent-self-edit-post";

        var create = await client.SendAsync(AuthPost(
            "/api/forum/posts",
            sub,
            new { title = "clean", body = "clean body", boardId = "general" },
            accountType: "agent"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var postId = (await ReadRootAsync(create)).GetProperty("data").GetProperty("id").GetString()!;

        var edit = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{postId}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { body = "updated with ARENA-BLOCK" }),
                Encoding.UTF8,
                "application/json"),
        };
        edit.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken(sub, accountType: "agent"));
        var res = await client.SendAsync(edit);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var persisted = await (await PostsAsync(factory)).Find(x => x.Id == postId).FirstOrDefaultAsync();
        Assert.Contains("ARENA-BLOCK", persisted!.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Agent_self_edit_reply_with_blocked_word_succeeds()
    {
        using var factory = _fx.CreateFactory(ConfigureRejectWord);
        using var client = factory.CreateClient();
        const string sub = "agent-self-edit-reply";

        var create = await client.SendAsync(AuthPost(
            "/api/forum/posts/aspam-post-1/replies",
            sub,
            new { body = "clean reply" },
            accountType: "agent"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var replyId = (await ReadRootAsync(create)).GetProperty("data").GetProperty("id").GetString()!;

        var edit = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/aspam-post-1/replies/{replyId}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { body = "reply ARENA-BLOCK now" }),
                Encoding.UTF8,
                "application/json"),
        };
        edit.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken(sub, accountType: "agent"));
        var res = await client.SendAsync(edit);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var persisted = await (await RepliesAsync(factory)).Find(x => x.Id == replyId).FirstOrDefaultAsync();
        Assert.Contains("ARENA-BLOCK", persisted!.Body, StringComparison.OrdinalIgnoreCase);
    }
}
