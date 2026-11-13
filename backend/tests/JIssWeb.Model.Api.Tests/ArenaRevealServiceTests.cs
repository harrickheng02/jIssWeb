using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

/// <summary>极简 IOptions&lt;T&gt; 桩，避免测试项目额外包版本冲突。</summary>
internal sealed class OptionsStub<T> : IOptions<T> where T : class, new()
{
    public OptionsStub(T value) => Value = value;
    public T Value { get; }
}

[Collection("ArenaReveal")]
public class ArenaRevealServiceTests
{
    private readonly ArenaRevealIntegrationFixture _fx;

    public ArenaRevealServiceTests(ArenaRevealIntegrationFixture fx) => _fx = fx;

    private ArenaRevealService CreateService()
    {
        var client = _fx.GetClient();
        IOptions<MongoSettings> opts = new OptionsStub<MongoSettings>(
            new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<ArenaRevealOptions> arenaOpts = new OptionsStub<ArenaRevealOptions>(new ArenaRevealOptions());
        // #34：默认 Enabled=false，悬赏能力零写入、积分恰好回落 50，既有断言无需改动。
        IOptions<ArenaBountyOptions> bountyOpts = new OptionsStub<ArenaBountyOptions>(new ArenaBountyOptions());
        IArenaBountyService bounty = new ArenaBountyService(
            client, opts, bountyOpts, NullLogger<ArenaBountyService>.Instance);
        return new ArenaRevealService(
            client,
            opts,
            arenaOpts,
            new StubConfessionGenerator(ArenaConfessionTemplate.StaticBody("stub")),
            new PassBlockedWordFilter(),
            NullLogger<ArenaRevealService>.Instance,
            bounty);
    }

    private IMongoCollection<ArenaRevealJobRecord> Jobs()
        => _fx.GetClient().GetDatabase(_fx.DatabaseName)
            .GetCollection<ArenaRevealJobRecord>(ArenaMongoSetup.RevealJobsCollectionName);

    [Fact]
    public async Task TryClaimWeek_同weekKey重复领取返回NotOwned且只建一条()
    {
        await _fx.ClearAsync();
        var svc = CreateService();
        var first = await svc.TryClaimWeekAsync("2026-06-01");
        var second = await svc.TryClaimWeekAsync("2026-06-01");

        Assert.Equal(ClaimOutcome.Owned, first);
        Assert.Equal(ClaimOutcome.NotOwned, second);

        var filter = Builders<ArenaRevealJobRecord>.Filter.Eq(x => x.WeekKey, "2026-06-01");
        Assert.Equal(1, await Jobs().CountDocumentsAsync(filter));
        var job = await Jobs().Find(filter).FirstAsync();
        Assert.Equal("2026-06-01", job.WeekKey);
        Assert.Equal("settling", job.State);
    }

    [Fact]
    public async Task RevealWeek_首次处理推进到completed_再次调用跳过()
    {
        await _fx.ClearAsync();
        var svc = CreateService();

        var processed = await svc.RevealWeekAsync("2026-06-08");
        Assert.Equal(RevealOutcome.Processed, processed);

        var filter = Builders<ArenaRevealJobRecord>.Filter.Eq(x => x.WeekKey, "2026-06-08");
        var job = await Jobs().Find(filter).FirstAsync();
        Assert.Equal("completed", job.State);
        Assert.NotNull(job.PublishedAtUtc);

        var skipped = await svc.RevealWeekAsync("2026-06-08");
        Assert.Equal(RevealOutcome.Skipped, skipped);
    }
}
