using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

[BsonIgnoreExtraElements]
public sealed class AgentLlmProfileRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
