using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

[Collection("ArenaReveal")]
public class AgentExperienceDecayTests : IAsyncLifetime
{
    private readonly ArenaRevealIntegrationFixture _fx;
    public AgentExperienceDecayTests(ArenaRevealIntegrationFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.ClearAsync();
        AgentMongoSetup.EnsureIndexes(Db());
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private IMongoDatabase Db() => _fx.GetClient().GetDatabase(_fx.DatabaseName);
    private IMongoCollection<AgentExperienceRecord> Exps() => Db().GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName);
    private IMongoCollection<AgentPersonaRecord> Personas() => Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);

    private AgentExperienceDecayService Create(bool enabled = true, double decay = 0.85, double bonus = 1.2)
    {
        IOptions<MongoSettings> mongo = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<AgentExperienceInheritanceOptions> opts = new OptionsStub<AgentExperienceInheritanceOptions>(
            new AgentExperienceInheritanceOptions { Enabled = enabled, DecayFactor = decay, SurvivalBonusFactor = bonus, RecentInheritDays = 30, NormalizeDays = 28 });
        return new AgentExperienceDecayService(_fx.GetClient(), mongo, opts, NullLogger<AgentExperienceDecayService>.Instance);
    }

    private static AgentExperienceRecord Exp(string id, double weight = 1.0, DateTime? inheritedAt = null, string source = "src", params string[] inheritedBy) => new()
    {
        Id = id, ExperienceId = id, SourcePersonaId = source, PatternType = "style", Description = "d",
        Weight = weight, InheritedBy = inheritedBy.ToList(), LastInheritedAtUtc = inheritedAt, CreatedAtUtc = DateTime.UtcNow.AddDays(-60),
    };

    [Fact]
    public async Task RunMonthAsync_DecaysAllByIdefactor_AndIsIdempotentPerMonth()
    {
        Exps().InsertMany(new[] { Exp("a"), Exp("b", 2.0) });
        var svc = Create();
        await svc.RunMonthAsync("2026-01");
        var a = await Exps().Find(x => x.Id == "a").FirstAsync();
        var b = await Exps().Find(x => x.Id == "b").FirstAsync();
        Assert.Equal(0.85, a.Weight, 3);
        Assert.Equal(1.7, b.Weight, 3);

        // 同月重复：11000 静默跳过，不再衰减。
        await svc.RunMonthAsync("2026-01");
        a = await Exps().Find(x => x.Id == "a").FirstAsync();
        Assert.Equal(0.85, a.Weight, 3);

        // 次月再衰减一次。
        await svc.RunMonthAsync("2026-02");
        a = await Exps().Find(x => x.Id == "a").FirstAsync();
        Assert.Equal(0.85 * 0.85, a.Weight, 3);
    }

    [Fact]
    public async Task RunMonthAsync_RecentlyInheritedHighSurvival_GetsBonus()
    {
        Personas().InsertOne(new AgentPersonaRecord { Id = "hi", AgentUserId = "hi", PersonaId = "hi-p", Nickname = "hi", SurvivalDays = 40 });
        Exps().InsertMany(new[]
        {
            Exp("bonused", inheritedAt: DateTime.UtcNow.AddDays(-3), source: "hi"),
            Exp("plain", inheritedAt: DateTime.UtcNow.AddDays(-3), source: "low-survivor"),
            Exp("old-inherit", inheritedAt: DateTime.UtcNow.AddDays(-60), source: "hi"),
        });
        var svc = Create();
        await svc.RunMonthAsync("2026-03");
        Assert.Equal(0.85 * 1.2, (await Exps().Find(x => x.Id == "bonused").FirstAsync()).Weight, 3);
        Assert.Equal(0.85, (await Exps().Find(x => x.Id == "plain").FirstAsync()).Weight, 3);
        Assert.Equal(0.85, (await Exps().Find(x => x.Id == "old-inherit").FirstAsync()).Weight, 3);
    }

    [Fact]
    public async Task RunMonthAsync_DisabledOrEmpty_WritesNothing()
    {
        Exps().InsertOne(Exp("x"));
        var off = Create(enabled: false);
        await off.RunMonthAsync("2026-04");
        Assert.Equal(1.0, (await Exps().Find(x => x.Id == "x").FirstAsync()).Weight, 3);
        Assert.Equal(0, await Db().GetCollection<AgentExperienceDecayState>(AgentMongoSetup.ExperienceDecayStateCollectionName)
            .CountDocumentsAsync(FilterDefinition<AgentExperienceDecayState>.Empty));

        await _fx.ClearAsync();
        var on = Create();
        await on.RunMonthAsync("2026-05"); // 空集合：仍认领月份但零条目写入
        Assert.Equal(0, await Exps().CountDocumentsAsync(FilterDefinition<AgentExperienceRecord>.Empty));
    }

    [Fact]
    public void YearMonth_HelpersWork()
    {
        Assert.Equal("2026-01", AgentExperienceDecayService.AddYearMonth("2025-12", 1));
        Assert.Equal("2027-01", AgentExperienceDecayService.AddYearMonth("2026-12", 1));
    }
}
