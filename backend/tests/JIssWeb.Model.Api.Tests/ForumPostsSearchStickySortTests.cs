using System.Net;
using System.Text.Json;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// Keyword search must not apply sticky-first ordering; <see cref="ForumPostRecord.IsSticky"/> is for display only (m3 / forum-content-api).
/// </summary>
[Collection("ForumMe")]
public class ForumPostsSearchStickySortTests
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumPostsSearchStickySortTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task List_Search_OrdersByRecencyNotSticky()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var t = DateTime.UtcNow;
        const string token = "SearchStickySortTokenZ9k2m";
        await posts.InsertManyAsync(new[]
        {
            new ForumPostRecord
            {
                Id = "search-sort-sticky-old",
                Title = $"{token} older sticky",
                Body = "b",
                Excerpt = "b",
                AuthorSubId = "user-a",
                Board = "综合",
                Tags = new List<string>(),
                IsSticky = true,
                CreatedAtUtc = t.AddHours(-3),
                LikeCount = 0,
                CommentCount = 0,
                ViewCount = 0,
            },
            new ForumPostRecord
            {
                Id = "search-sort-plain-new",
                Title = $"{token} newer plain",
                Body = "b",
                Excerpt = "b",
                AuthorSubId = "user-a",
                Board = "综合",
                Tags = new List<string>(),
                IsSticky = false,
                CreatedAtUtc = t.AddHours(-1),
                LikeCount = 0,
                CommentCount = 0,
                ViewCount = 0,
            },
        });

        try
        {
            var res = await _fx.Client.GetAsync(
                $"/api/forum/posts?q={Uri.EscapeDataString(token)}&page=1&pageSize=20");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("data").GetProperty("items");
            Assert.True(items.GetArrayLength() >= 2);
            Assert.Equal("search-sort-plain-new", items[0].GetProperty("id").GetString());
            Assert.Equal("search-sort-sticky-old", items[1].GetProperty("id").GetString());
        }
        finally
        {
            await posts.DeleteOneAsync(x => x.Id == "search-sort-sticky-old");
            await posts.DeleteOneAsync(x => x.Id == "search-sort-plain-new");
        }
    }
}
