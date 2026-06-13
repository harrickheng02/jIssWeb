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
public sealed class ForumBlockedWordLocalCreateTests
{
    private readonly ForumAntiSpamIntegrationFixture _fx;

    public ForumBlockedWordLocalCreateTests(ForumAntiSpamIntegrationFixture fx) => _fx = fx;

    private HttpRequestMessage AuthPost(string url, string sub, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub));
        return req;
    }

    private static void ConfigureBlockedWords(IWebHostBuilder b, string word, string handling = "local")
    {
        b.UseSetting("Forum:BlockedWords:Enabled", "true");
        b.UseSetting("Forum:BlockedWords:Handling", handling);
        b.UseSetting("Forum:BlockedWords:Words:0", word);
    }

    private static async Task<long> CountPostsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var dbName = factory.Services.GetRequiredService<IOptions<MongoSettings>>().Value.DatabaseName;
        return await mongo.GetDatabase(dbName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName).CountDocumentsAsync(FilterDefinition<ForumPostRecord>.Empty);
    }

    [Fact]
    public async Task CreatePost_LocalHandling_Returns200WithoutPersisting()
    {
        using var factory = _fx.CreateFactory(b => ConfigureBlockedWords(b, "badword"));
        using var client = factory.CreateClient();
        var before = await CountPostsAsync(factory);

        var res = await client.SendAsync(AuthPost("/api/forum/posts", "local-author",
            new { title = "Title BADWORD here", body = "clean", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var text = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("localOnly").GetBoolean());
        Assert.Equal("local", data.GetProperty("state").GetString());
        Assert.StartsWith("local:", data.GetProperty("id").GetString());
        Assert.DoesNotContain("badword", text, StringComparison.OrdinalIgnoreCase);

        var after = await CountPostsAsync(factory);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CreateReply_LocalHandling_Returns200WithoutPersisting()
    {
        using var factory = _fx.CreateFactory(b => ConfigureBlockedWords(b, "evil"));
        using var client = factory.CreateClient();
        var res = await client.SendAsync(AuthPost("/api/forum/posts/aspam-post-1/replies", "local-replier",
            new { body = "Evil content" }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("localOnly").GetBoolean());
        Assert.Equal("local", data.GetProperty("state").GetString());
    }

    [Fact]
    public async Task CreatePost_RejectHandling_Returns400()
    {
        using var factory = _fx.CreateFactory(b => ConfigureBlockedWords(b, "spam", "reject"));
        using var client = factory.CreateClient();
        var res = await client.SendAsync(AuthPost("/api/forum/posts", "reject-user",
            new { title = "fine", body = "contains SPAM token", boardId = "general" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("BLOCKED_CONTENT", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LocalCreate_DoesNotConsumePostRateLimit()
    {
        using var factory = _fx.CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "1");
            b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
            ConfigureBlockedWords(b, "blocked");
        });
        using var client = factory.CreateClient();

        var first = await client.SendAsync(AuthPost("/api/forum/posts", "rl-local",
            new { title = "ok", body = "ok", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var localHit = await client.SendAsync(AuthPost("/api/forum/posts", "rl-local",
            new { title = "blocked word", body = "x", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, localHit.StatusCode);
        using (var doc = JsonDocument.Parse(await localHit.Content.ReadAsStringAsync()))
            Assert.True(doc.RootElement.GetProperty("data").GetProperty("localOnly").GetBoolean());

        var second = await client.SendAsync(AuthPost("/api/forum/posts", "rl-local",
            new { title = "second ok", body = "ok", boardId = "general" }));
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
    }
}
