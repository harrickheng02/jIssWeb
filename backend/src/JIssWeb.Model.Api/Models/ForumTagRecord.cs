using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

[BsonIgnoreExtraElements]
public class ForumTagRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = ForumTagStatuses.Active;
    public int UseCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBySub { get; set; } = "";
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBySub { get; set; }
}

public static class ForumTagStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
}
