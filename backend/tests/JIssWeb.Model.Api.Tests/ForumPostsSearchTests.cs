using System.Net;
using System.Text.Json;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public class ForumPostsSearchTests
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumPostsSearchTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task List_SearchWhitespaceQ_Returns400()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=%20%20");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_SEARCH_QUERY", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_SearchByTitle_ReturnsMatchingPost()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=ta&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("me-post-a", items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task List_SearchByAuthorSub_ReturnsMatchingPost()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=user-b&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("me-post-b", items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task List_SearchWithBoardId_ReturnsIntersection()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=ta&boardId=general&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("me-post-a", items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task List_SearchWithBoardId_ExcludesOtherBoard()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=techunique&boardId=general&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_SearchWithBoardId_MatchesConfiguredBoard()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=techunique&boardId=tech&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("me-post-tech", items[0].GetProperty("id").GetString());
    }
}
