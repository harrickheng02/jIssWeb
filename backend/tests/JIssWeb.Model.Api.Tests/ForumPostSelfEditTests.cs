using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ForumPostSelfEditTests
{
    private readonly ForumMeIntegrationFixture _fx;
    public ForumPostSelfEditTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private async Task<string> SeedPublishedPostAsync(string authorSub, List<string>? tags = null)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var id = "self-edit-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = id, Title = "original title", Body = "original body", Excerpt = "original body",
            AuthorSubId = authorSub, Board = "综合", State = "published",
            Tags = tags ?? new List<string>(), CreatedAtUtc = DateTime.UtcNow,
        });
        return id;
    }

    private async Task DeletePostAsync(string postId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        await posts.DeleteOneAsync(x => x.Id == postId);
    }

    private async Task DeleteTagsByNameAsync(params string[] names)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var tags = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
        await tags.DeleteManyAsync(Builders<ForumTagRecord>.Filter.In(x => x.Name, names));
    }

    [Fact]
    public async Task Author_edit_post_returns_200_and_sets_UpdatedAtUtc()
    {
        var pid = await SeedPublishedPostAsync("user-a");
        try
        {
            var body = JsonSerializer.Serialize(new { title = "new title", body = "new body" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            using var scope = _fx.Factory.Services.CreateScope();
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
            var updated = await posts.Find(x => x.Id == pid).FirstOrDefaultAsync();
            Assert.NotNull(updated);
            Assert.Equal("new title", updated!.Title);
            Assert.NotNull(updated.UpdatedAtUtc);
        }
        finally
        {
            await DeletePostAsync(pid);
        }
    }

    [Fact]
    public async Task Non_author_edit_post_returns_403()
    {
        var pid = await SeedPublishedPostAsync("user-a");
        try
        {
            var body = JsonSerializer.Serialize(new { title = "hacked" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally
        {
            await DeletePostAsync(pid);
        }
    }

    [Fact]
    public async Task Edit_deleted_post_returns_404()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var pid = "self-edit-deleted-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid, Title = "x", Body = "y", Excerpt = "y",
            AuthorSubId = "user-a", Board = "综合", State = "deleted", CreatedAtUtc = DateTime.UtcNow,
        });

        try
        {
            var body = JsonSerializer.Serialize(new { title = "t" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        finally
        {
            await DeletePostAsync(pid);
        }
    }

    [Fact]
    public async Task Edit_published_post_rejects_board_change()
    {
        var pid = await SeedPublishedPostAsync("user-a");
        try
        {
            var body = JsonSerializer.Serialize(new { title = "t", boardId = "tech" });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        finally
        {
            await DeletePostAsync(pid);
        }
    }

    [Fact]
    public async Task Edit_post_tags_applies_use_count_delta()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var tags = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
        var now = DateTime.UtcNow;
        await tags.InsertManyAsync(new[]
        {
            new ForumTagRecord { Id = "delta-a", Name = "delta-a", Slug = "delta-a", Status = ForumTagStatuses.Active, UseCount = 5, CreatedAtUtc = now, CreatedBySub = "user-admin" },
            new ForumTagRecord { Id = "delta-b", Name = "delta-b", Slug = "delta-b", Status = ForumTagStatuses.Active, UseCount = 3, CreatedAtUtc = now, CreatedBySub = "user-admin" },
            new ForumTagRecord { Id = "delta-c", Name = "delta-c", Slug = "delta-c", Status = ForumTagStatuses.Active, UseCount = 2, CreatedAtUtc = now, CreatedBySub = "user-admin" },
        });

        var pid = await SeedPublishedPostAsync("user-a", new List<string> { "delta-a", "delta-b" });
        try
        {
            var body = JsonSerializer.Serialize(new { tags = new[] { "delta-b", "delta-c" } });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var tagA = await tags.Find(x => x.Name == "delta-a").FirstOrDefaultAsync();
            var tagB = await tags.Find(x => x.Name == "delta-b").FirstOrDefaultAsync();
            var tagC = await tags.Find(x => x.Name == "delta-c").FirstOrDefaultAsync();
            Assert.NotNull(tagA);
            Assert.NotNull(tagB);
            Assert.NotNull(tagC);
            Assert.Equal(4, tagA!.UseCount);
            Assert.Equal(3, tagB!.UseCount);
            Assert.Equal(3, tagC!.UseCount);
        }
        finally
        {
            await DeletePostAsync(pid);
            await DeleteTagsByNameAsync("delta-a", "delta-b", "delta-c");
        }
    }

    [Fact]
    public async Task Edit_post_removing_unregistered_tag_succeeds_without_tag_writes()
    {
        var pid = await SeedPublishedPostAsync("user-a", new List<string> { "alpha", "ghost-tag-xyz" });
        try
        {
            var body = JsonSerializer.Serialize(new { tags = new[] { "alpha" } });
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
            var r = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            using var scope = _fx.Factory.Services.CreateScope();
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var tagCol = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
            var ghost = await tagCol.Find(x => x.Name == "ghost-tag-xyz").FirstOrDefaultAsync();
            Assert.Null(ghost);
        }
        finally
        {
            await DeletePostAsync(pid);
        }
    }
}
