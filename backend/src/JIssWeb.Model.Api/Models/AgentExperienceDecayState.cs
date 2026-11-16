using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 经验权重月度衰减幂等标记（#38，按 yyyy-MM 唯一）。
/// </summary>
[BsonIgnoreExtraElements]
public sealed class AgentExperienceDecayState
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string YearMonth { get; set; } = "";

    public DateTime RunAtUtc { get; set; } = DateTime.UtcNow;
}
