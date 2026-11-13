using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

[CollectionDefinition("AgentMemory")]
public sealed class AgentMemoryCollection : ICollectionFixture<AgentMemoryWebFixture>
{
}

public sealed class AgentMemoryWebFixture : IAsyncLifetime
{
    private MongoDbRunner _runner = null!;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public AgentSchedulerEngine Engine => Factory.Services.GetRequiredService<AgentSchedulerEngine>();

    public async Task InitializeAsync()
    {
        _runner = MongoDbRunner.Start();
        var dbName = "agent_memory_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _runner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", dbName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
            b.UseSetting("AgentScheduler:Enabled", "false");
            b.UseSetting("AgentScheduler:MemoryEnabled", "true");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
                services.RemoveAll<ILlmChatClient>();
                services.AddSingleton<ILlmChatClient>(new StubLlmChatClient());
                services.RemoveAll<IAgentForumHttpClient>();
                services.AddSingleton<IAgentForumHttpClient>(new StubForumClient());
                services.RemoveAll<IAgentUserInternalClient>();
                services.AddSingleton<IAgentUserInternalClient>(new StubUserClient());
            });
        });
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        _runner.Dispose();
    }

    public IMongoDatabase Database()
    {
        var mongo = Factory.Services.GetRequiredService<IMongoClient>();
        var dbName = Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoSettings>>().Value.DatabaseName;
        return mongo.GetDatabase(dbName);
    }

    public async Task ClearAsync()
    {
        var db = Database();
        foreach (var name in new[] { AgentMongoSetup.PersonasCollectionName, AgentMongoSetup.SchedulerStateCollectionName, AgentMongoSetup.LlmProfilesCollectionName, AgentMongoSetup.ExperiencesCollectionName, ForumMongoSetup.PostsCollectionName, ForumMongoSetup.RepliesCollectionName })
            await db.GetCollection<MongoDB.Bson.BsonDocument>(name).DeleteManyAsync(MongoDB.Driver.FilterDefinition<MongoDB.Bson.BsonDocument>.Empty);
    }

    public async Task<(string PersonaId, string AgentUserId)> SeedAgentAsync()
    {
        var db = Database();
        var personaId = "persona-" + Guid.NewGuid().ToString("N")[..8];
        var agentUserId = "agent-" + Guid.NewGuid().ToString("N")[..8];
        var profileId = "profile-" + Guid.NewGuid().ToString("N")[..8];
        await db.GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName).InsertOneAsync(new AgentLlmProfileRecord
        {
            Id = profileId, Name = "test", BaseUrl = "https://api.deepseek.com", ApiKey = "x", Model = "deepseek-chat", Enabled = true, CreatedAtUtc = DateTime.UtcNow,
        });
        await db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).InsertOneAsync(new AgentPersonaRecord
        {
            Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId, Nickname = "Ada", LlmProfileId = profileId,
            State = AgentPersonaState.Active, RelationshipMemory = new(), StanceLog = new(), ExperienceIds = new(),
        });
        await db.GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName).InsertOneAsync(new AgentSchedulerStateRecord
        {
            Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId, Paused = false,
            AllowAutoPost = true, AllowAutoReply = true, QuotaDateUtc = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            PostsUsed = 0, RepliesUsed = 0, NextDueAtUtc = DateTime.UtcNow.AddMinutes(-1),
        });
        return (personaId, agentUserId);
    }

    public async Task SeedHumanPostAsync(string authorSubId, string board = "general")
    {
        var db = Database();
        await db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName).InsertOneAsync(new ForumPostRecord
        {
            Id = "post-" + Guid.NewGuid().ToString("N")[..8], AuthorSubId = authorSubId, Title = "人类的热帖", Body = "内容",
            State = "published", Board = board, CommentCount = 10, LikeCount = 10, CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
        });
    }

    public async Task<AgentPersonaRecord?> GetPersonaAsync(string agentUserId)
        => await Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .Find(x => x.Id == agentUserId).FirstOrDefaultAsync();

    public async Task<List<MongoDB.Bson.BsonDocument>> ListExperiencesAsync()
        => await Database().GetCollection<MongoDB.Bson.BsonDocument>(AgentMongoSetup.ExperiencesCollectionName).Find(MongoDB.Driver.FilterDefinition<MongoDB.Bson.BsonDocument>.Empty).ToListAsync();
}

[Collection("AgentMemory")]
public sealed class AgentMemoryIntegrationTests
{
    private readonly AgentMemoryWebFixture _fx;
    public AgentMemoryIntegrationTests(AgentMemoryWebFixture fx) => _fx = fx;

    [Fact]
    public async Task I1_reply_updates_relationship_memory()
    {
        await _fx.ClearAsync();
        var (personaId, agentUserId) = await _fx.SeedAgentAsync();
        await _fx.SeedHumanPostAsync("human-target");
        var result = await _fx.Engine.TriggerAsync(personaId, AgentScheduledAction.Reply);
        Assert.True(result.Succeeded);

        var p = await _fx.GetPersonaAsync(agentUserId);
        Assert.NotNull(p);
        Assert.True(p!.RelationshipMemory.ContainsKey("human-target"));
        Assert.Equal("友好、乐于讨论", p.RelationshipMemory["human-target"]);
    }

    [Fact]
    public async Task I2_post_updates_stance_log()
    {
        await _fx.ClearAsync();
        var (personaId, agentUserId) = await _fx.SeedAgentAsync();
        var result = await _fx.Engine.TriggerAsync(personaId, AgentScheduledAction.Post);
        Assert.True(result.Succeeded);

        var p = await _fx.GetPersonaAsync(agentUserId);
        Assert.NotNull(p);
        Assert.NotEmpty(p!.StanceLog);
        Assert.Equal("乐观支持", p.StanceLog["人工智能"]);
    }

    [Fact]
    public async Task I3_reply_updates_stance_log()
    {
        await _fx.ClearAsync();
        var (personaId, agentUserId) = await _fx.SeedAgentAsync();
        await _fx.SeedHumanPostAsync("human-target");
        await _fx.Engine.TriggerAsync(personaId, AgentScheduledAction.Reply);

        var p = await _fx.GetPersonaAsync(agentUserId);
        Assert.NotNull(p);
        Assert.Equal("乐观支持", p!.StanceLog["人工智能"]);
    }

    [Fact]
    public async Task I5_same_user_same_week_dedup_keeps_single_relationship_entry()
    {
        await _fx.ClearAsync();
        var (personaId, agentUserId) = await _fx.SeedAgentAsync();
        await _fx.SeedHumanPostAsync("human-target");
        await _fx.Engine.TriggerAsync(personaId, AgentScheduledAction.Reply);
        await _fx.Engine.TriggerAsync(personaId, AgentScheduledAction.Reply);

        var p = await _fx.GetPersonaAsync(agentUserId);
        Assert.NotNull(p);
        Assert.Single(p!.RelationshipMemory); // 同一 human-target 仅一条
        Assert.Equal("友好、乐于讨论", p.RelationshipMemory["human-target"]);
    }

    [Fact]
    public async Task I9_experiences_indexes_created_at_startup()
    {
        await _fx.ClearAsync();
        var db = _fx.Database();
        var names = (await db.GetCollection<MongoDB.Bson.BsonDocument>(AgentMongoSetup.ExperiencesCollectionName).Indexes.ListAsync())
            .ToList().Select(x => x["name"].AsString).ToList();
        Assert.Contains("weight_desc", names);
        Assert.Contains("sourcePersona_weight", names);
        Assert.Contains("inheritedBy", names);
        Assert.Contains("uniq_experienceId", names);
    }
}
