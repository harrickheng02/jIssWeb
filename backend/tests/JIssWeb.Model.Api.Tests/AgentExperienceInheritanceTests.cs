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

public sealed class AgentExperiencePromptTests
{
    private static AgentPersonaRecord Persona() => new() { Nickname = "Ada", Prompt = "冷静。" };
    private static AgentMemoryContext Mem() => new(
        new Dictionary<string, string> { ["u"] = "友好" },
        new Dictionary<string, string>(),
        null);
    private static List<AgentExperienceRecord> Exps() => new()
    {
        new() { ExperienceId = "e1", Description = "主动提问", ExampleTexts = new() { "你怎么看？" } },
    };

    [Fact]
    public void InheritedSegment_AppearsAfterMemoryAndBeforeInstruction()
    {
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildPostPrompt(Persona(), new[] { "近期" }, "general", Mem(), Exps());
        Assert.Contains("经社区验证的交流策略", prompt);
        Assert.Contains("主动提问", prompt);
        var memIdx = prompt.IndexOf("你的人际记忆：", StringComparison.Ordinal);
        var expIdx = prompt.IndexOf("经社区验证的交流策略", StringComparison.Ordinal);
        var insIdx = prompt.IndexOf("请为板块", StringComparison.Ordinal);
        Assert.True(memIdx < expIdx && expIdx < insIdx, $"ordering wrong: mem={memIdx} exp={expIdx} ins={insIdx}");
    }

    [Fact]
    public void NoInherited_OmitsSegment()
    {
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildPostPrompt(Persona(), new[] { "近期" }, "general", Mem(), null);
        Assert.DoesNotContain("经社区验证的交流策略", prompt);
    }

    [Fact]
    public void EmptyInherited_OmitsSegment()
    {
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildReplyPrompt(Persona(), new[] { "近期" }, new ForumPostRecord { Title = "t", Body = "b" }, inheritedExperiences: new List<AgentExperienceRecord>());
        Assert.DoesNotContain("经社区验证的交流策略", prompt);
    }
}

[Collection("ArenaReveal")]
public class AgentExperienceInheritanceIntegrationTests : IAsyncLifetime
{
    private readonly ArenaRevealIntegrationFixture _fx;
    public AgentExperienceInheritanceIntegrationTests(ArenaRevealIntegrationFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.ClearAsync();
        AgentMongoSetup.EnsureIndexes(Db());
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private IMongoDatabase Db() => _fx.GetClient().GetDatabase(_fx.DatabaseName);
    private IMongoCollection<AgentExperienceRecord> Exps() => Db().GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName);
    private IMongoCollection<AgentPersonaRecord> Personas() => Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);

    private ExperienceInheritanceService Create(bool enabled = true, int topN = 5)
    {
        IOptions<MongoSettings> mongo = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<AgentExperienceInheritanceOptions> opts = new OptionsStub<AgentExperienceInheritanceOptions>(
            new AgentExperienceInheritanceOptions { Enabled = enabled, TopN = topN });
        return new ExperienceInheritanceService(_fx.GetClient(), mongo, opts);
    }

    private static AgentExperienceRecord Exp(string id, double weight) => new()
    {
        Id = id, ExperienceId = id, SourcePersonaId = "src", PatternType = "style", Description = "d",
        Weight = weight, InheritedBy = new(), CreatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task ApplyAsync_TopNByWeight_AndUpdatesInheritedBy()
    {
        Exps().InsertMany(new[] { Exp("e1", 1.0), Exp("e2", 3.0), Exp("e3", 2.0) });
        var record = new AgentPersonaRecord { Id = "new", AgentUserId = "new", PersonaId = "new-p", Nickname = "n" };
        await Create(topN: 2).ApplyAsync(record);
        Assert.Equal(new[] { "e2", "e3" }, record.ExperienceIds);
        Assert.Single((await Exps().Find(x => x.Id == "e2").FirstAsync()).InheritedBy);
        Assert.Contains("new", (await Exps().Find(x => x.Id == "e2").FirstAsync()).InheritedBy);
        Assert.NotNull((await Exps().Find(x => x.Id == "e2").FirstAsync()).LastInheritedAtUtc);

        // 幂等：再次 Apply 不重复累加 inheritedBy。
        await Create(topN: 2).ApplyAsync(record);
        Assert.Single((await Exps().Find(x => x.Id == "e2").FirstAsync()).InheritedBy);
    }

    [Fact]
    public async Task ApplyAsync_SwitchOff_NoExperienceButGenerationStillAssigned()
    {
        Exps().InsertOne(Exp("e1", 1.0));
        Personas().InsertOne(new AgentPersonaRecord { Id = "gen2", AgentUserId = "gen2", PersonaId = "gen2-p", Nickname = "g", Generation = 2 });
        var record = new AgentPersonaRecord { Id = "new", AgentUserId = "new", PersonaId = "new-p", Nickname = "n" };
        await Create(enabled: false).ApplyAsync(record, new[] { "gen2-p" });
        Assert.Empty(record.ExperienceIds);
        Assert.Equal(3, record.Generation);
        Assert.Equal(new[] { "gen2-p" }, record.InheritedFrom);
        Assert.Empty((await Exps().Find(x => x.Id == "e1").FirstAsync()).InheritedBy);
    }

    [Fact]
    public async Task ApplyAsync_GenerationMaxPlusOne_InvalidSourcesIgnored()
    {
        Personas().InsertMany(new[]
        {
            new AgentPersonaRecord { Id = "g1", AgentUserId = "g1", PersonaId = "g1-p", Nickname = "a", Generation = 1 },
            new AgentPersonaRecord { Id = "g3", AgentUserId = "g3", PersonaId = "g3-p", Nickname = "b", Generation = 3 },
        });
        var record = new AgentPersonaRecord { Id = "new", AgentUserId = "new", PersonaId = "new-p", Nickname = "n" };
        await Create(enabled: false).ApplyAsync(record, new[] { "g1-p", "g3-p", "missing", "new-p" });
        Assert.Equal(4, record.Generation);
        Assert.Equal(2, record.InheritedFrom.Count);
        Assert.DoesNotContain("missing", record.InheritedFrom);
        Assert.DoesNotContain("new-p", record.InheritedFrom);
    }

    [Fact]
    public async Task ApplyAsync_NoValidSource_GenerationOne()
    {
        var record = new AgentPersonaRecord { Id = "new", AgentUserId = "new", PersonaId = "new-p", Nickname = "n" };
        await Create(enabled: false).ApplyAsync(record, new List<string>());
        Assert.Equal(1, record.Generation);
        Assert.Empty(record.InheritedFrom);
    }

    [Fact]
    public async Task LoadAsync_ReadsByIds_AndDegradesOnMissing()
    {
        Exps().InsertMany(new[] { Exp("e1", 1.0), Exp("e2", 1.0) });
        var svc = Create();
        var loaded = await svc.LoadAsync(new[] { "e1", "e2", "missing" });
        Assert.Equal(2, loaded.Count);
        Assert.Empty(await svc.LoadAsync(new List<string>()));
    }
}
