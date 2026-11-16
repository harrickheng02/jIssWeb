using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 账号轮换器测试夹具：Mongo2Go + 真实 <see cref="AgentProvisioningService"/> 与
/// <see cref="ExperienceInheritanceService"/>，仅把 User.Api 账号接口替换为 Mock。
/// </summary>
public sealed class AgentRotationFixture : IAsyncLifetime
{
    private Mongo2Go.MongoDbRunner? _runner;

    public string ConnectionString { get; private set; } = "";
    public string DatabaseName { get; private set; } = "";
    public Mock<IAgentUserInternalClient> AgentUsers { get; } = new();

    public Task InitializeAsync()
    {
        _runner = Mongo2Go.MongoDbRunner.Start();
        ConnectionString = _runner.ConnectionString;
        DatabaseName = "model_agent_rotation_" + Guid.NewGuid().ToString("N");
        AgentMongoSetup.EnsureIndexes(new MongoClient(ConnectionString).GetDatabase(DatabaseName));

        AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ProvisionedAgent($"agent-{Guid.NewGuid():N}", "test-token"));
        AgentUsers.Setup(x => x.DisableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _runner?.Dispose();
        return Task.CompletedTask;
    }

    public IMongoClient Client() => new MongoClient(ConnectionString);

    public IMongoDatabase Db() => Client().GetDatabase(DatabaseName);

    public async Task ClearAsync()
    {
        var db = Db();
        foreach (var name in new[]
                 {
                     AgentMongoSetup.PersonasCollectionName,
                     AgentMongoSetup.LlmProfilesCollectionName,
                     AgentMongoSetup.SchedulerStateCollectionName,
                     AgentMongoSetup.ExperiencesCollectionName,
                     AgentMongoSetup.RotationRunsCollectionName,
                 })
        {
            await db.GetCollection<BsonDocument>(name).DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        }
    }

    public async Task<string> EnsureProfileAsync(bool enabled = true)
    {
        var id = Guid.NewGuid().ToString("N");
        await Db().GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName)
            .InsertOneAsync(new AgentLlmProfileRecord
            {
                Id = id,
                Name = $"rot-{id[..8]}",
                BaseUrl = "https://api.deepseek.com",
                ApiKey = "test-key-1234",
                Model = "deepseek-chat",
                Enabled = enabled,
                CreatedAtUtc = DateTime.UtcNow,
            });
        return id;
    }

    public async Task<AgentPersonaRecord> InsertPersonaAsync(
        string state, string? llmProfileId, string nickname = "Ada", int generation = 1,
        DateTime? updatedAtUtc = null)
    {
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        var persona = new AgentPersonaRecord
        {
            Id = agentUserId,
            AgentUserId = agentUserId,
            PersonaId = $"persona-{Guid.NewGuid():N}",
            Nickname = nickname,
            Model = AgentModelIds.Deepseek,
            LlmProfileId = llmProfileId,
            Generation = generation,
            State = state,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            UpdatedAtUtc = updatedAtUtc,
        };
        await Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .InsertOneAsync(persona);
        return persona;
    }

    public AgentRotationService CreateRotation(AgentRotationOptions options)
    {
        var mongoSettings = new OptionsStub<MongoSettings>(new MongoSettings { ConnectionString = ConnectionString, DatabaseName = DatabaseName });
        var inheritance = new ExperienceInheritanceService(
            Client(), mongoSettings,
            new OptionsStub<AgentExperienceInheritanceOptions>(new AgentExperienceInheritanceOptions { Enabled = false }));
        var provisioning = new AgentProvisioningService(
            Client(), mongoSettings,
            new OptionsStub<AgentSchedulerOptions>(new AgentSchedulerOptions()),
            AgentUsers.Object, inheritance,
            NullLogger<AgentProvisioningService>.Instance);

        return new AgentRotationService(
            Client(), mongoSettings,
            new OptionsStub<AgentRotationOptions>(options),
            provisioning, NullLogger<AgentRotationService>.Instance);
    }

    public AgentRotationBackgroundService CreateBackground(AgentRotationOptions options)
        => new(CreateRotation(options),
            new OptionsStub<AgentRotationOptions>(options),
            NullLogger<AgentRotationBackgroundService>.Instance);

    public IMongoCollection<AgentPersonaRecord> Personas()
        => Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);

    public IMongoCollection<AgentRotationRunRecord> Runs()
        => Db().GetCollection<AgentRotationRunRecord>(AgentMongoSetup.RotationRunsCollectionName);

    public IMongoCollection<AgentSchedulerStateRecord> SchedulerStates()
        => Db().GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName);
}
