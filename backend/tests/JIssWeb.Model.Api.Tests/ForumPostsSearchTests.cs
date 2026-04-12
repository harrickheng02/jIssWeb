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

    [Fact]
    public async Task List_TagWhitespace_Returns400()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?tag=%20&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_TAG_QUERY", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_FilterByTag_CaseInsensitive_ReturnsMatchingPosts()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?tag=alpha&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        var ids = new HashSet<string?>
        {
            items[0].GetProperty("id").GetString(),
            items[1].GetProperty("id").GetString(),
        };
        Assert.Contains("me-post-a", ids);
        Assert.Contains("me-post-tech", ids);
    }

    [Fact]
    public async Task List_TagWithBoard_ReturnsIntersection()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?tag=shared&boardId=general&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
    }

    [Fact]
    public async Task List_TagAndSearchQ_ReturnsIntersection()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=ta&tag=alpha&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("me-post-a", items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task List_TagAndSearchQ_NoMatchWhenTagExcludesTitleMatch()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=ta&tag=techtag&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_TagSearchQAndBoard_ReturnsTripleIntersection()
    {
        var res = await _fx.Client.GetAsync("/api/forum/posts?q=ta&tag=alpha&boardId=general&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("me-post-a", items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task PopularTags_NoBoard_ReturnsOrdered()
    {
        var res = await _fx.Client.GetAsync("/api/forum/tags/popular?limit=10");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetArrayLength() >= 2);
        Assert.Equal("alpha", data[0].GetString());
        Assert.Equal("shared", data[1].GetString());
    }

    [Fact]
    public async Task PopularTags_InvalidBoard_Returns400()
    {
        var res = await _fx.Client.GetAsync("/api/forum/tags/popular?boardId=unknown");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
