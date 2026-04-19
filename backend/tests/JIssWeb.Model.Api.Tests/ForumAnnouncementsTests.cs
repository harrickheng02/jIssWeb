using System.Net;
using System.Text.Json;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public class ForumAnnouncementsTests
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumAnnouncementsTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task List_ReturnsPinnedFirst_ThenByDate()
    {
        var res = await _fx.Client.GetAsync("/api/forum/announcements?limit=10");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
        Assert.Equal("ann-pinned", data[0].GetProperty("id").GetString());
        Assert.Equal("ann-normal", data[1].GetProperty("id").GetString());
        Assert.True(data[0].GetProperty("pinned").GetBoolean());
    }

    [Fact]
    public async Task List_InvalidLimit_Returns400()
    {
        var res = await _fx.Client.GetAsync("/api/forum/announcements?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_PAGINATION", doc.RootElement.GetProperty("code").GetString());
    }
}

[Collection("ForumAnnouncementsEmpty")]
public class ForumAnnouncementsEmptyTests
{
    private readonly ForumAnnouncementsEmptyIntegrationFixture _fx;

    public ForumAnnouncementsEmptyTests(ForumAnnouncementsEmptyIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task List_Empty_Returns200AndEmptyArray()
    {
        var res = await _fx.Client.GetAsync("/api/forum/announcements");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetArrayLength());
    }
}
