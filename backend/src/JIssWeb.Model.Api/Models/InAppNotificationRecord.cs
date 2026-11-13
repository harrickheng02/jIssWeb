using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

public static class InAppNotificationTypes
{
    public const string ReplyToPost = "ReplyToPost";
    public const string ReportResolved = "ReportResolved";
    public const string ReportAcknowledged = "ReportAcknowledged";
    public const string ForumWarning = "ForumWarning";
    /// <summary>图灵场揭露成功（+50）通知；仅 hit 目标的有效指认者收到，零通知分支不发送。</summary>
    public const string ArenaRevealHit = "ArenaRevealHit";
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
    /// <summary>图灵场揭露批次 id（= reveal_jobs.Id）。非 arena 通知为 null；门禁据此隐藏未发布的批次通知（D6）。</summary>
    [BsonIgnoreIfNull] public string? BatchId { get; set; }
    /// <summary>积分事件幂等 id（=(weekId,userId,targetUserId,type) 派生）。uniq_eventId 唯一索引保证重放幂等（D9）。</summary>
    [BsonIgnoreIfNull] public string? EventId { get; set; }
    /// <summary>批次发布（reveal_jobs.state=completed）后由门禁统一置位；置位前对读取路径不可见（D6）。</summary>
    [BsonIgnoreIfNull] public DateTime? ReleasedAtUtc { get; set; }
}
