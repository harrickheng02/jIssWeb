using System.Globalization;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

[Collection("ArenaReveal")]
public class AgentExperienceExtractionTests : IAsyncLifetime
{
    private const string WeekKey = "2026-07-06";
    private readonly ArenaRevealIntegrationFixture _fx;
    public AgentExperienceExtractionTests(ArenaRevealIntegrationFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.ClearAsync();
        AgentMongoSetup.EnsureIndexes(Db()); // 幂等唯一索引（experienceId）供幂等写入测试
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private IMongoDatabase Db() => _fx.GetClient().GetDatabase(_fx.DatabaseName);
    private IMongoCollection<AgentExperienceRecord> Exps()
        => Db().GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName);
    private IMongoCollection<AgentPersonaRecord> Personas()
        => Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);

    private static DateTime WeekEndUtc => new DateTimeOffset(
        DateTime.ParseExact(WeekKey, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(7),
        TimeSpan.FromHours(8)).ToUniversalTime().UtcDateTime;

    [Fact]
    public void Parse_ExtractsStructuredExperiences()
    {
        const string raw = "```json\n{\"experiences\":[" +
            "{\"patternType\":\"relationship_building\",\"description\":\"主动提问\",\"exampleTexts\":[\"你怎么看？\"],\"topicContext\":\"AI伦理\"}," +
            "{\"patternType\":\"invalid\",\"description\":\"应被过滤\"}]}\n```";
        var items = LlmExperienceExtractor.Parse(raw);
        Assert.Single(items);
        Assert.Equal("relationship_building", items[0].PatternType);
        Assert.Equal("AI伦理", items[0].TopicContext);
    }

    [Fact]
    public void Parse_ReturnsEmptyOnGarbage()
    {
        Assert.Empty(LlmExperienceExtractor.Parse("no json here"));
        Assert.Empty(LlmExperienceExtractor.Parse(null));
    }

    [Fact]
    public async Task ExtractAsync_ProducesNormalizedEntries_AndIsIdempotent()
    {
        var client = _fx.GetClient();
        var created = WeekEndUtc.AddDays(-28);
        Personas().InsertOne(new AgentPersonaRecord { Id = "winner", AgentUserId = "winner", PersonaId = "winner-p", Nickname = "冠军", Model = AgentModelIds.Deepseek, CreatedAtUtc = created, Generation = 2 });
        Db().GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName).InsertOne(new ForumPostRecord { Id = "p1", AuthorSubId = "winner", Title = "标题", Body = "正文", CreatedAtUtc = created.AddDays(1) });

        IOptions<MongoSettings> mongo = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<AgentExperienceInheritanceOptions> expOpts = new OptionsStub<AgentExperienceInheritanceOptions>(
            new AgentExperienceInheritanceOptions { Enabled = true, NormalizeDays = 28 });
        var llm = new StubExperienceLlm();
        IAgentLlmConfigResolver config = new StubLlmConfigResolver();
        var extractor = new LlmExperienceExtractor(client, mongo, config, llm, expOpts, NullLogger<LlmExperienceExtractor>.Instance);

        var medalAt = WeekEndUtc;
        Assert.True(await extractor.ExtractAsync("winner", WeekKey, medalAt));
        var entries = await Exps().Find(x => x.SourcePersonaId == "winner").ToListAsync();
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.Equal(1.0, e.EffectivenessScore, 3); // 28 天 / 28 基准 = 1.0
            Assert.Equal(1.0, e.Weight, 3);
            Assert.Equal(2, e.Generation);
            Assert.StartsWith("exp-winner-", e.ExperienceId);
        });
        var countFirst = entries.Count;

        // 重复触发：唯一键冲突静默跳过，不新增。
        await extractor.ExtractAsync("winner", WeekKey, medalAt);
        Assert.Equal(countFirst, await Exps().CountDocumentsAsync(x => x.SourcePersonaId == "winner"));
    }

    [Fact]
    public async Task ExtractAsync_SwitchOff_WritesNothing()
    {
        var client = _fx.GetClient();
        Personas().InsertOne(new AgentPersonaRecord { Id = "winner2", AgentUserId = "winner2", PersonaId = "winner2-p", Nickname = "n", CreatedAtUtc = WeekEndUtc.AddDays(-28) });
        Db().GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName).InsertOne(new ForumPostRecord { Id = "p2", AuthorSubId = "winner2", Title = "t", Body = "b", CreatedAtUtc = WeekEndUtc.AddDays(-1) });

        IOptions<MongoSettings> mongo = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<AgentExperienceInheritanceOptions> expOpts = new OptionsStub<AgentExperienceInheritanceOptions>(new AgentExperienceInheritanceOptions { Enabled = false });
        var extractor = new LlmExperienceExtractor(client, mongo, new StubLlmConfigResolver(), new StubExperienceLlm(), expOpts, NullLogger<LlmExperienceExtractor>.Instance);

        Assert.False(await extractor.ExtractAsync("winner2", WeekKey, WeekEndUtc));
        Assert.Equal(0, await Exps().CountDocumentsAsync(x => x.SourcePersonaId == "winner2"));
    }

    [Fact]
    public async Task MedalTrigger_FiresOnce_NotOnSurvivor_NotWhenDisabled()
    {
        var client = _fx.GetClient();
        IOptions<MongoSettings> mongo = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });

        // 首次奖章（MedalWeeks=4）触发一次。
        Personas().InsertOne(new AgentPersonaRecord { Id = "preyA", AgentUserId = "preyA", PersonaId = "preyA-p", Nickname = "a", CreatedAtUtc = WeekEndUtc.AddDays(-28) });
        var spy = new SpyExperienceExtractor();
        var bounty = new ArenaBountyService(client, mongo,
            new OptionsStub<ArenaBountyOptions>(new ArenaBountyOptions { Enabled = true }),
            NullLogger<ArenaBountyService>.Instance, spy);
        await bounty.ComputeWeekAsync(WeekKey);
        Assert.Equal(1, spy.Calls);
        await bounty.ComputeWeekAsync(WeekKey); // 同周重复不重复触发
        Assert.Equal(1, spy.Calls);

        // 开关关闭：零触发。
        Personas().InsertOne(new AgentPersonaRecord { Id = "preyB", AgentUserId = "preyB", PersonaId = "preyB-p", Nickname = "b", CreatedAtUtc = WeekEndUtc.AddDays(-28) });
        var spyOff = new SpyExperienceExtractor();
        var bountyOff = new ArenaBountyService(client, mongo,
            new OptionsStub<ArenaBountyOptions>(new ArenaBountyOptions { Enabled = false }),
            NullLogger<ArenaBountyService>.Instance, spyOff);
        await bountyOff.ComputeWeekAsync(WeekKey);
        Assert.Equal(0, spyOff.Calls);
    }

    [Fact]
    public async Task ExtractAsync_LlmFailure_DegradesWithoutThrow()
    {
        var client = _fx.GetClient();
        Personas().InsertOne(new AgentPersonaRecord { Id = "winner3", AgentUserId = "winner3", PersonaId = "winner3-p", Nickname = "n", CreatedAtUtc = WeekEndUtc.AddDays(-28) });
        Db().GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName).InsertOne(new ForumPostRecord { Id = "p3", AuthorSubId = "winner3", Title = "t", Body = "b", CreatedAtUtc = WeekEndUtc.AddDays(-1) });

        IOptions<MongoSettings> mongo = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<AgentExperienceInheritanceOptions> expOpts = new OptionsStub<AgentExperienceInheritanceOptions>(new AgentExperienceInheritanceOptions { Enabled = true });
        var extractor = new LlmExperienceExtractor(client, mongo, new StubLlmConfigResolver(), new ThrowingLlm(), expOpts, NullLogger<LlmExperienceExtractor>.Instance);

        Assert.False(await extractor.ExtractAsync("winner3", WeekKey, WeekEndUtc));
    }
}

internal sealed class StubExperienceLlm : ILlmChatClient
{
    public const string Json = "{\"experiences\":[{\"patternType\":\"style\",\"description\":\"语气稳定\",\"exampleTexts\":[\"样例\"]}]}";
    public Task<string> CompleteAsync(LlmChatRequest request, CancellationToken ct = default) => Task.FromResult(Json);
}

internal sealed class ThrowingLlm : ILlmChatClient
{
    public Task<string> CompleteAsync(LlmChatRequest request, CancellationToken ct = default) => throw new InvalidOperationException("LLM down");
}

internal sealed class StubLlmConfigResolver : IAgentLlmConfigResolver
{
    public Task<bool> IsSchedulerEnabledAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task<AgentRuntimeConfigRecord?> GetRuntimeAsync(CancellationToken ct = default) => Task.FromResult<AgentRuntimeConfigRecord?>(null);
    public Task<ResolvedLlmProfile?> ResolveProfileAsync(string? profileId, CancellationToken ct = default)
        => Task.FromResult<ResolvedLlmProfile?>(new ResolvedLlmProfile("", "stub", "", "https://api.deepseek.com", "deepseek-chat", true));
    public Task<EffectiveAgentLlmConfig> GetEffectiveAsync(CancellationToken ct = default) => Task.FromResult(new EffectiveAgentLlmConfig("", "", "", false));
}

/// <summary>奖章触发间谍：记录 ExtractAsync 调用次数，用于验证「首次奖章触发 / 幸存者不触发 / 续跑不重复」。</summary>
internal sealed class SpyExperienceExtractor : IAgentExperienceExtractor
{
    public int Calls { get; private set; }
    public Task<bool> ExtractAsync(string agentUserId, string medalWeekKey, DateTime medalAtUtc, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(true);
    }
}
