using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// Integration tests for the featured feed: GET /api/forum/posts?featured=true
/// Uses its own fixture to seed known featured posts with controlled timestamps.
/// </summary>
public class ForumPostsFeaturedFeedTests : IClassFixture<ForumPostsFeaturedFeedFixture>
{
    private readonly ForumPostsFeaturedFeedFixture _fx;
    public ForumPostsFeaturedFeedTests(ForumPostsFeaturedFeedFixture fx) => _fx = fx;

    [Fact]
    public async Task Featured_true_returns_only_featured_posts()
    {
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=true&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        foreach (var item in items.EnumerateArray())
            Assert.True(item.GetProperty("isFeatured").GetBoolean(), "All items in featured feed must have isFeatured=true");
    }

    [Fact]
    public async Task Featured_true_returns_correct_count()
    {
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=true&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var totalCount = doc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32();
        Assert.Equal(2, totalCount); // feed-featured-a and feed-featured-b
    }

    [Fact]
    public async Task Featured_feed_ordered_by_FeaturedAtUtc_descending()
    {
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=true&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        // feed-featured-b has FeaturedAtUtc newer than feed-featured-a
        Assert.Equal("feed-featured-b", items[0].GetProperty("id").GetString());
        Assert.Equal("feed-featured-a", items[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Non_featured_posts_excluded_from_featured_feed()
    {
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=true&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var ids = items.Select(x => x.GetProperty("id").GetString()).ToHashSet();
        Assert.DoesNotContain("feed-normal-post", ids);
    }

    [Fact]
    public async Task Featured_combined_with_boardId_filter()
    {
        // feed-featured-a is in 综合, feed-featured-b is in 技术
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=true&boardId=general&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("feed-featured-a", items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Featured_combined_with_tag_filter()
    {
        // feed-featured-a has tag "精华讨论"; feed-featured-b has no tags
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=true&tag=精华讨论&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("feed-featured-a", items[0].GetProperty("id").GetString());
        Assert.True(items[0].GetProperty("isFeatured").GetBoolean());
    }

    [Fact]
    public async Task Featured_false_returns_only_non_featured_posts()
    {
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=false&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        foreach (var item in items)
            Assert.False(item.GetProperty("isFeatured").GetBoolean(), "featured=false must exclude featured posts");
        var ids = items.Select(x => x.GetProperty("id").GetString()).ToHashSet();
        Assert.Contains("feed-normal-post", ids);
    }

    [Fact]
    public async Task Invalid_featured_param_returns_400()
    {
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=notabool&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("INVALID_FEATURED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Featured_sort_not_overridden_by_sort_param()
    {
        // Even if sort=hot is passed together with featured=true, featured sort wins
        var r = await _fx.Client.GetAsync("/api/forum/posts?featured=true&sort=hot&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        // Still ordered by FeaturedAtUtc desc: b first, a second
        Assert.Equal("feed-featured-b", items[0].GetProperty("id").GetString());
        Assert.Equal("feed-featured-a", items[1].GetProperty("id").GetString());
    }
}

public sealed class ForumPostsFeaturedFeedFixture : IAsyncLifetime
{
    private Mongo2Go.MongoDbRunner? _mongoRunner;

    public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_featured_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
            b.UseSetting("Forum:Boards:1:Id", "tech");
            b.UseSetting("Forum:Boards:1:Title", "技术");
            b.UseSetting("Forum:Moderation:Moderators:0:Sub", "user-mod");
            b.UseSetting("Forum:Moderation:Moderators:0:BoardIds:0", "general");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
            });
        });
        Client = Factory.CreateClient();
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);

        var t = DateTime.UtcNow;
        await posts.InsertManyAsync(new[]
        {
            new ForumPostRecord
            {
                Id = "feed-featured-a",
                Title = "featured post A",
                Body = "body a",
                Excerpt = "body a",
                AuthorSubId = "user-a",
                Board = "综合",
                Tags = new List<string> { "精华讨论" },
                CreatedAtUtc = t.AddMinutes(-30),
                IsFeatured = true,
                FeaturedAtUtc = t.AddMinutes(-20),   // older
                FeaturedBySub = "user-admin",
            },
            new ForumPostRecord
            {
                Id = "feed-featured-b",
                Title = "featured post B",
                Body = "body b",
                Excerpt = "body b",
                AuthorSubId = "user-b",
                Board = "技术",
                Tags = new List<string>(),
                CreatedAtUtc = t.AddMinutes(-25),
                IsFeatured = true,
                FeaturedAtUtc = t.AddMinutes(-10),   // newer → should appear first
                FeaturedBySub = "user-admin",
            },
            new ForumPostRecord
            {
                Id = "feed-normal-post",
                Title = "normal post C",
                Body = "body c",
                Excerpt = "body c",
                AuthorSubId = "user-c",
                Board = "综合",
                Tags = new List<string>(),
                CreatedAtUtc = t.AddMinutes(-5),
                IsFeatured = false,
            },
        });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }
}
