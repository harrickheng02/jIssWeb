using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.User.Api.Models;

public class ProfileRecordDoc
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";

    public string OwnerUserId { get; set; } = "";

    public string? Nickname { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? Gender { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
