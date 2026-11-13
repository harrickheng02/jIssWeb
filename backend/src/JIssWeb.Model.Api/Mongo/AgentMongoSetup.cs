using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Mongo;

public static class AgentMongoSetup
{
    public const string PersonasCollectionName = "agent_personas";
    public const string ExperiencesCollectionName = "agent_experiences";
    public const string SchedulerStateCollectionName = "agent_scheduler_state";
    public const string RuntimeConfigCollectionName = "agent_runtime_config";
    public const string LlmProfilesCollectionName = "agent_llm_profiles";

    public static void EnsureIndexes(IMongoDatabase db)
    {
        var personas = db.GetCollection<AgentPersonaRecord>(PersonasCollectionName);
        personas.Indexes.CreateOne(new CreateIndexModel<AgentPersonaRecord>(
            Builders<AgentPersonaRecord>.IndexKeys.Ascending(x => x.PersonaId),
            new CreateIndexOptions { Unique = true, Name = "uniq_personaId" }));
        personas.Indexes.CreateOne(new CreateIndexModel<AgentPersonaRecord>(
            Builders<AgentPersonaRecord>.IndexKeys.Ascending(x => x.State),
            new CreateIndexOptions { Name = "state" }));
        personas.Indexes.CreateOne(new CreateIndexModel<AgentPersonaRecord>(
            Builders<AgentPersonaRecord>.IndexKeys.Ascending(x => x.LlmProfileId),
            new CreateIndexOptions { Name = "llmProfileId", Sparse = true }));

        var experiences = db.GetCollection<AgentExperienceRecord>(ExperiencesCollectionName);
        experiences.Indexes.CreateOne(new CreateIndexModel<AgentExperienceRecord>(
            Builders<AgentExperienceRecord>.IndexKeys.Descending(x => x.Weight),
            new CreateIndexOptions { Name = "weight_desc" }));
        experiences.Indexes.CreateOne(new CreateIndexModel<AgentExperienceRecord>(
            Builders<AgentExperienceRecord>.IndexKeys.Ascending(x => x.SourcePersonaId).Descending(x => x.Weight),
            new CreateIndexOptions { Name = "sourcePersona_weight" }));
        experiences.Indexes.CreateOne(new CreateIndexModel<AgentExperienceRecord>(
            Builders<AgentExperienceRecord>.IndexKeys.Ascending(x => x.InheritedBy),
            new CreateIndexOptions { Name = "inheritedBy" }));
        experiences.Indexes.CreateOne(new CreateIndexModel<AgentExperienceRecord>(
            Builders<AgentExperienceRecord>.IndexKeys.Ascending(x => x.ExperienceId),
            new CreateIndexOptions { Unique = true, Name = "uniq_experienceId" }));

        var states = db.GetCollection<AgentSchedulerStateRecord>(SchedulerStateCollectionName);
        states.Indexes.CreateOne(new CreateIndexModel<AgentSchedulerStateRecord>(
            Builders<AgentSchedulerStateRecord>.IndexKeys.Ascending(x => x.PersonaId),
            new CreateIndexOptions { Unique = true, Name = "uniq_scheduler_personaId" }));
        states.Indexes.CreateOne(new CreateIndexModel<AgentSchedulerStateRecord>(
            Builders<AgentSchedulerStateRecord>.IndexKeys.Ascending(x => x.NextDueAtUtc).Ascending(x => x.Paused),
            new CreateIndexOptions { Name = "nextDue_paused" }));

        var profiles = db.GetCollection<AgentLlmProfileRecord>(LlmProfilesCollectionName);
        profiles.Indexes.CreateOne(new CreateIndexModel<AgentLlmProfileRecord>(
            Builders<AgentLlmProfileRecord>.IndexKeys.Ascending(x => x.Name),
            new CreateIndexOptions { Name = "name" }));
    }

    public static async Task MigrateLegacyLlmAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var mongo = services.GetRequiredService<IMongoClient>();
        var mongoOpts = services.GetRequiredService<IOptions<MongoSettings>>().Value;
        var schedulerOpts = services.GetRequiredService<IOptions<AgentSchedulerOptions>>().Value;
        var db = mongo.GetDatabase(mongoOpts.DatabaseName);
        var configs = db.GetCollection<AgentRuntimeConfigRecord>(RuntimeConfigCollectionName);
        var profiles = db.GetCollection<AgentLlmProfileRecord>(LlmProfilesCollectionName);
        var personas = db.GetCollection<AgentPersonaRecord>(PersonasCollectionName);

        var runtime = await configs.Find(x => x.Id == AgentRuntimeConfigRecord.SingletonId).FirstOrDefaultAsync(ct);
        var profileCount = await profiles.CountDocumentsAsync(FilterDefinition<AgentLlmProfileRecord>.Empty, cancellationToken: ct);

        string? migratedProfileId = null;
        if (profileCount == 0)
        {
            var key = !string.IsNullOrWhiteSpace(runtime?.DeepSeekApiKey)
                ? runtime!.DeepSeekApiKey!.Trim()
                : schedulerOpts.DeepSeek.ApiKey?.Trim() ?? "";
            var baseUrl = !string.IsNullOrWhiteSpace(runtime?.DeepSeekBaseUrl)
                ? runtime!.DeepSeekBaseUrl!.Trim()
                : schedulerOpts.DeepSeek.BaseUrl;
            var model = !string.IsNullOrWhiteSpace(runtime?.DeepSeekModel)
                ? runtime!.DeepSeekModel!.Trim()
                : schedulerOpts.DeepSeek.Model;

            if (!string.IsNullOrWhiteSpace(key))
            {
                var now = DateTime.UtcNow;
                var profile = new AgentLlmProfileRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "迁移自全局 DeepSeek",
                    BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.deepseek.com" : baseUrl,
                    ApiKey = key,
                    Model = string.IsNullOrWhiteSpace(model) ? "deepseek-chat" : model,
                    Enabled = true,
                    CreatedAtUtc = now,
                };
                await profiles.InsertOneAsync(profile, cancellationToken: ct);
                migratedProfileId = profile.Id;
            }
        }

        if (runtime is not null &&
            (!string.IsNullOrWhiteSpace(runtime.DeepSeekApiKey)
             || !string.IsNullOrWhiteSpace(runtime.DeepSeekBaseUrl)
             || !string.IsNullOrWhiteSpace(runtime.DeepSeekModel)))
        {
            runtime.DeepSeekApiKey = null;
            runtime.DeepSeekBaseUrl = null;
            runtime.DeepSeekModel = null;
            runtime.UpdatedAtUtc = DateTime.UtcNow;
            await configs.ReplaceOneAsync(x => x.Id == runtime.Id, runtime, new ReplaceOptions { IsUpsert = true }, ct);
        }

        if (migratedProfileId is not null)
        {
            await personas.UpdateManyAsync(
                x => x.LlmProfileId == null || x.LlmProfileId == "",
                Builders<AgentPersonaRecord>.Update
                    .Set(x => x.LlmProfileId, migratedProfileId)
                    .Set(x => x.Model, AgentModelIds.Deepseek)
                    .Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: ct);
        }
    }
}
