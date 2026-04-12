using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public class ForumMeEndpointsTests
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumMeEndpointsTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task MyPosts_WithoutBearer_Returns401()
    {
        var res = await _fx.Client.GetAsync("/api/forum/me/posts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task MyReplies_WithoutBearer_Returns401()
    {
        var res = await _fx.Client.GetAsync("/api/forum/me/replies");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task MyPosts_InvalidPage_Returns400()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/forum/me/posts?page=0");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertJsonCodeAsync(res, "INVALID_PAGINATION");
    }

    [Fact]
    public async Task MyReplies_InvalidPageSize_Returns400()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/forum/me/replies?page=1&pageSize=0");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertJsonCodeAsync(res, "INVALID_PAGINATION");
    }

    [Fact]
    public async Task MyPosts_UserA_ReturnsOnlyOwnPosts()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/forum/me/posts?page=1&pageSize=20");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("me-post-a", items[0].GetProperty("id").GetString());
        Assert.Equal("user-a", items[0].GetProperty("authorId").GetString());
    }

    [Fact]
    public async Task Create_TagsTooMany_Returns400()
    {
        var tags = Enumerable.Range(0, 11).Select(i => $"u{i}").ToArray();
        var body = JsonSerializer.Serialize(new { title = "t", body = "b", boardId = "general", tags });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertJsonCodeAsync(res, "INVALID_TAGS");
    }

    [Fact]
    public async Task Create_TagTooLong_Returns400()
    {
        var longTag = new string('c', 33);
        var body = JsonSerializer.Serialize(new { title = "t", body = "b", boardId = "general", tags = new[] { longTag } });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await AssertJsonCodeAsync(res, "INVALID_TAGS");
    }

    [Fact]
    public async Task MyReplies_UserA_ReturnsOnlyOwnReplies()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/forum/me/replies?page=1&pageSize=20");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("me-reply-a", items[0].GetProperty("id").GetString());
        Assert.Equal("user-a", items[0].GetProperty("authorId").GetString());
    }

    private static async Task AssertJsonCodeAsync(HttpResponseMessage res, string code)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }
}
