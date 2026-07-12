using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

[BsonIgnoreExtraElements]
public sealed class AgentPersonaRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    public string PersonaId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string AgentUserId { get; set; } = "";
    public string Model { get; set; } = AgentModelIds.Doubao;
    public string? Personality { get; set; }
    public List<string> Interests { get; set; } = new();
    public AgentPostingStyle PostingStyle { get; set; } = new();
    public Dictionary<string, string> RelationshipMemory { get; set; } = new();
    public Dictionary<string, string> StanceLog { get; set; } = new();
    public int Generation { get; set; } = 1;
    public List<string> InheritedFrom { get; set; } = new();
    public List<string> ExperienceIds { get; set; } = new();
    public int SurvivalDays { get; set; }
    public string State { get; set; } = AgentPersonaState.Active;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class AgentPostingStyle
{
    public string? Length { get; set; }
    public string? Emoji { get; set; }
    public List<string> Catchphrases { get; set; } = new();
}

public static class AgentPersonaState
{
    public const string Active = "active";
    public const string Eliminated = "eliminated";
    public const string Archived = "archived";

    public static bool IsValid(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is Active or Eliminated or Archived;
    }
}

public static class AgentModelIds
{
    public const string Doubao = "doubao";
    public const string Deepseek = "deepseek";

    public static bool IsValid(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is Doubao or Deepseek;
    }
}
