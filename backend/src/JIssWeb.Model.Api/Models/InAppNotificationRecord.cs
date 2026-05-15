using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

public static class InAppNotificationTypes
{
    public const string ReplyToPost = "ReplyToPost";
    public const string ReportResolved = "ReportResolved";
}

public class InAppNotificationRecord
{
    public string Id { get; set; } = "";
    public string RecipientSubId { get; set; } = "";
    public string Type { get; set; } = InAppNotificationTypes.ReplyToPost;
    public string PostId { get; set; } = "";
    [BsonIgnoreIfNull] public string? ReplyId { get; set; }
    [BsonIgnoreIfNull] public string? ReportId { get; set; }
    public string ActorSubId { get; set; } = "";
    public string PostTitle { get; set; } = "";
    public DateTime? ReadAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
