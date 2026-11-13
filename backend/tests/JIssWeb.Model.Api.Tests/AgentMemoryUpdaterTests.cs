using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mongo2Go;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

internal sealed class FakeMemoryExtractor : IAgentMemoryExtractor
{
    private readonly MemoryExtraction? _result;
    public FakeMemoryExtractor(MemoryExtraction? result) => _result = result;
    public Task<MemoryExtraction?> ExtractAsync(ResolvedLlmProfile profile, string body, string? topicHint, CancellationToken ct)
        => Task.FromResult(_result);
}

public sealed class AgentMemoryUpdaterTests : IAsyncLifetime
{
    private MongoDbRunner _runner = null!;
    private IMongoDatabase _db = null!;
    private IMongoClient _client = null!;
    private const string AgentUserId = "agent-updater";

    public async Task InitializeAsync()
    {
        _runner = MongoDbRunner.Start();
        _client = new MongoClient(_runner.ConnectionString);
        var dbName = "updater_" + Guid.NewGuid().ToString("N");
        _db = _client.GetDatabase(dbName);
        AgentMongoSetup.EnsureIndexes(_db);
        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _runner.Dispose();
        return Task.CompletedTask;
    }

    private AgentMemoryUpdater MakeUpdater(AgentSchedulerOptions options, IAgentMemoryExtractor extractor)
        => new(_client, Microsoft.Extensions.Options.Options.Create(new MongoSettings { DatabaseName = _db.DatabaseNamespace.DatabaseName }), extractor, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AgentMemoryUpdater>.Instance);

    private async Task SeedPersonaAsync()
    {
        await _db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).DeleteManyAsync(x => x.Id == AgentUserId);
        await _db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).InsertOneAsync(new AgentPersonaRecord
        {
            Id = AgentUserId, AgentUserId = AgentUserId, PersonaId = "p1", Nickname = "Ada", State = AgentPersonaState.Active,
            RelationshipMemory = new(), StanceLog = new(), ExperienceIds = new(),
        });
    }

    private async Task<AgentPersonaRecord?> GetPersonaAsync()
        => await _db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).Find(x => x.Id == AgentUserId).FirstOrDefaultAsync();

    private async Task ClearExperiencesAsync()
        => await _db.GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName).DeleteManyAsync(FilterDefinition<AgentExperienceRecord>.Empty);

    private AgentPersonaRecord Persona() => new() { Id = AgentUserId, Nickname = "Ada", AgentUserId = AgentUserId, PersonaId = "p1", State = AgentPersonaState.Active };

    [Fact]
    public async Task I8_disabled_does_not_write_memory()
    {
        await SeedPersonaAsync();
        var updater = MakeUpdater(new AgentSchedulerOptions { MemoryEnabled = false }, new FakeMemoryExtractor(null));
        await updater.UpdateAsync(Persona(), AgentScheduledAction.Reply, "human-x", "正文", "general",
            new ResolvedLlmProfile("p", "n", "k", "u", "m", true), default);

        var p = await GetPersonaAsync();
        Assert.Empty(p!.RelationshipMemory);
        Assert.Empty(p.StanceLog);
    }

    [Fact]
    public async Task I14_degraded_extraction_still_writes_fallback_signals()
    {
        await SeedPersonaAsync();
        var updater = MakeUpdater(new AgentSchedulerOptions { MemoryEnabled = true }, new FakeMemoryExtractor(null));
        await updater.UpdateAsync(Persona(), AgentScheduledAction.Reply, "human-x", "正文", "general",
            new ResolvedLlmProfile("p", "n", "k", "u", "m", true), default);

        var p = await GetPersonaAsync();
        Assert.Equal("曾互动", p!.RelationshipMemory["human-x"]);
        Assert.Equal("曾参与讨论", p.StanceLog["general"]);
        Assert.NotNull(p.UpdatedAtUtc);
    }

    [Fact]
    public async Task I10_pattern_detection_writes_experience_and_appends_id()
    {
        await SeedPersonaAsync();
        await ClearExperiencesAsync();
        var extraction = new MemoryExtraction("友好", new[] { new ExtractedStance("人工智能", "乐观") }, PatternType: "style", Description: "稳定风格");
        var updater = MakeUpdater(new AgentSchedulerOptions { MemoryEnabled = true }, new FakeMemoryExtractor(extraction));
        await updater.UpdateAsync(Persona(), AgentScheduledAction.Post, null, "这是一段发言正文", "general",
            new ResolvedLlmProfile("p", "n", "k", "u", "m", true), default);

        var p = await GetPersonaAsync();
        Assert.Equal("乐观", p!.StanceLog["人工智能"]);
        var exps = await _db.GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName).Find(x => x.SourcePersonaId == AgentUserId).ToListAsync();
        Assert.Single(exps);
        var exp = exps[0];
        Assert.Equal("style", exp.PatternType);
        Assert.Equal("稳定风格", exp.Description);
        Assert.Equal(1.0, exp.EffectivenessScore);
        Assert.Equal(1.0, exp.Weight);
        Assert.Empty(exp.InheritedBy);
        Assert.Contains(exp.ExperienceId, p.ExperienceIds);
    }

    [Fact]
    public async Task I11_two_pattern_writes_append_distinct_experience_ids()
    {
        await SeedPersonaAsync();
        await ClearExperiencesAsync();
        var extraction = new MemoryExtraction("友好", Array.Empty<ExtractedStance>(), PatternType: "style", Description: "d");
        var updater = MakeUpdater(new AgentSchedulerOptions { MemoryEnabled = true }, new FakeMemoryExtractor(extraction));
        await updater.UpdateAsync(Persona(), AgentScheduledAction.Post, null, "正文1", "general", new ResolvedLlmProfile("p", "n", "k", "u", "m", true), default);
        await updater.UpdateAsync(Persona(), AgentScheduledAction.Post, null, "正文2", "general", new ResolvedLlmProfile("p", "n", "k", "u", "m", true), default);

        var p = await GetPersonaAsync();
        Assert.Equal(2, p!.ExperienceIds.Count);
        var exps = await _db.GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName).Find(x => x.SourcePersonaId == AgentUserId).ToListAsync();
        Assert.Equal(2, exps.Count);
    }

    [Fact]
    public async Task I12_experienceId_unique_index_rejects_duplicate()
    {
        await ClearExperiencesAsync();
        var coll = _db.GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName);
        var rec = new AgentExperienceRecord
        {
            Id = Guid.NewGuid().ToString("N"), ExperienceId = "dup-x", SourcePersonaId = "s", PatternType = "style", CreatedAtUtc = DateTime.UtcNow,
        };
        await coll.InsertOneAsync(rec);
        var dup = new AgentExperienceRecord
        {
            Id = Guid.NewGuid().ToString("N"), ExperienceId = "dup-x", SourcePersonaId = "s", PatternType = "style", CreatedAtUtc = DateTime.UtcNow,
        };
        var ex = await Assert.ThrowsAsync<MongoWriteException>(() => coll.InsertOneAsync(dup));
        Assert.Equal(11000, ex.WriteError?.Code);
    }
}
