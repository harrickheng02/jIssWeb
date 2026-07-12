using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Mongo;

public static class AgentMongoSetup
{
    public const string PersonasCollectionName = "agent_personas";
    public const string ExperiencesCollectionName = "agent_experiences";

    public static void EnsureIndexes(IMongoDatabase db)
    {
        var personas = db.GetCollection<AgentPersonaRecord>(PersonasCollectionName);
        personas.Indexes.CreateOne(new CreateIndexModel<AgentPersonaRecord>(
            Builders<AgentPersonaRecord>.IndexKeys.Ascending(x => x.PersonaId),
            new CreateIndexOptions { Unique = true, Name = "uniq_personaId" }));
        personas.Indexes.CreateOne(new CreateIndexModel<AgentPersonaRecord>(
            Builders<AgentPersonaRecord>.IndexKeys.Ascending(x => x.State),
            new CreateIndexOptions { Name = "state" }));

        var experiences = db.GetCollection<AgentExperienceRecord>(ExperiencesCollectionName);
        experiences.Indexes.CreateOne(new CreateIndexModel<AgentExperienceRecord>(
            Builders<AgentExperienceRecord>.IndexKeys.Descending(x => x.Weight),
            new CreateIndexOptions { Name = "weight_desc" }));
        experiences.Indexes.CreateOne(new CreateIndexModel<AgentExperienceRecord>(
            Builders<AgentExperienceRecord>.IndexKeys.Ascending(x => x.PersonaId).Descending(x => x.Weight),
            new CreateIndexOptions { Name = "persona_weight" }));
    }
}
