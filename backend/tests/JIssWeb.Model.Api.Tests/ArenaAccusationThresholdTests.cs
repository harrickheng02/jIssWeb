using System.Net;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

[Collection("ArenaAccusationThreshold2")]
public class ArenaAccusationThresholdTests
{
    private readonly ArenaAccusationThreshold2IntegrationFixture _fx;

    public ArenaAccusationThresholdTests(ArenaAccusationThreshold2IntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task 阈值配置为2_两人指认即达_threshold_reached()
    {
        var t = "target-cfg2";
        foreach (var u in new[] { "cfg-a", "cfg-b" })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/arena/accusations");
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(u));
            req.Content = new StringContent(JsonSerializer.Serialize(new { targetUserId = t }),
                Encoding.UTF8, "application/json");
            var resp = await _fx.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        var db = _fx.GetDatabase();
        var tallies = db.GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName);
        var tally = await tallies.Find(FilterDefinition<ArenaTargetTallyRecord>.Empty).FirstAsync();
        Assert.Equal(2, tally.Count);
        Assert.Equal("threshold_reached", tally.Status);
    }
}
