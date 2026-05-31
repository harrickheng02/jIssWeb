using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ForumStateFilterTests
{
    private readonly ForumMeIntegrationFixture _fx;
    public ForumStateFilterTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private async Task<string> SeedPostWithStateAsync(string state, string? deletedBySub = null)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var id = "state-filter-" + Guid.NewGuid().ToString("N");
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = id, Title = "t", Body = "b", Excerpt = "b",
            AuthorSubId = "user-a", Board = "综合",
            State = state, CreatedAtUtc = DateTime.UtcNow,
            DeletedAtUtc = state == "deleted" ? DateTime.UtcNow : null,
            DeletedBySub = deletedBySub,
        });
        return id;
    }

    [Fact]
    public async Task Draft_post_not_in_public_list()
    {
        var id = await SeedPostWithStateAsync("draft");
        var r = await _fx.Client.GetAsync("/api/forum/posts?page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain(id, body);
    }

    [Fact]
    public async Task Deleted_post_returns_404_on_detail()
    {
        var id = await SeedPostWithStateAsync("deleted");
        var r = await _fx.Client.GetAsync($"/api/forum/posts/{id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Draft_post_returns_404_for_non_author()
    {
        var id = await SeedPostWithStateAsync("draft");
        // No auth = anonymous, not the author
        var r = await _fx.Client.GetAsync($"/api/forum/posts/{id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_GetById_deleted_post_returns_200_with_state()
    {
        var id = await SeedPostWithStateAsync("deleted", deletedBySub: "user-mod");

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/forum/posts/{id}");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("deleted", json.RootElement.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal("b", json.RootElement.GetProperty("data").GetProperty("body").GetString());
    }
}
