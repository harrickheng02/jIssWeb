using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JIssWeb.Frontend.Bff.Tests;

public class BffForumInitTests : IClassFixture<BffWebAppFactory>
{
    private readonly BffWebAppFactory _factory;

    public BffForumInitTests(BffWebAppFactory factory) => _factory = factory;

    private void SetupHappyPath()
    {
        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/boards").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = new[] { new { id = "general", title = "综合" } } }));

        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/announcements").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = Array.Empty<object>() }));

        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/posts").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20 } }));

        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/tags/popular").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = new[] { "vue", "dotnet" } }));
    }

    [Fact]
    public async Task ForumInit_AnonymousRequest_Returns200WithBoardsAndPosts()
    {
        SetupHappyPath();

        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/bff/forum-init");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("boards", out _));
        Assert.True(data.TryGetProperty("posts", out _));
        Assert.False(data.TryGetProperty("unreadCount", out _)); // unreadCount 由 /bff/me 提供，forum-init 不含此字段
    }

    [Fact]
    public async Task ForumInit_BoardIdPropagated_IncludesParamInDownstreamCalls()
    {
        SetupHappyPath();
        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/posts").WithParam("boardId", "tech").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20 } }));

        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/bff/forum-init?boardId=tech");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task ForumInit_OneDownstreamFails_ReturnsDegradedWithWarning()
    {
        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/boards").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = Array.Empty<object>() }));

        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/announcements").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503)); // fails

        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/posts").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20 } }));

        _factory.ModelApiMock
            .Given(Request.Create().WithPath("/api/forum/tags/popular").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = Array.Empty<string>() }));

        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/bff/forum-init");

        // Degraded response is still 200
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("warnings", out var warnings));
        Assert.Contains("announcements", warnings.EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task ForumInit_PostMethod_Returns405()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/bff/forum-init", null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, resp.StatusCode);
    }
}
