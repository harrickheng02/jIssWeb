using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

[BsonIgnoreExtraElements]
public sealed class AgentExperienceRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    public string PersonaId { get; set; } = "";
    public string AgentUserId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Summary { get; set; } = "";
    public double Weight { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
