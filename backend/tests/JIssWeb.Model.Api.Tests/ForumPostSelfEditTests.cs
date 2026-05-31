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

    [Fact]
    public async Task Author_edit_post_returns_200_and_sets_UpdatedAtUtc()
    {
        var pid = await SeedPublishedPostAsync("user-a");
        var body = JsonSerializer.Serialize(new { title = "new title", body = "new body" });
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // Verify DB
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var updated = await posts.Find(x => x.Id == pid).FirstOrDefaultAsync();
        Assert.NotNull(updated);
        Assert.Equal("new title", updated!.Title);
        Assert.NotNull(updated.UpdatedAtUtc);
    }

    [Fact]
    public async Task Non_author_edit_post_returns_403()
    {
        var pid = await SeedPublishedPostAsync("user-a");
        var body = JsonSerializer.Serialize(new { title = "hacked" });
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b"));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
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

        var body = JsonSerializer.Serialize(new { title = "t" });
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/forum/posts/{pid}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
