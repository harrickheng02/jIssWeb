using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Customer.Api.Models;

public class ProfileRecord
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
