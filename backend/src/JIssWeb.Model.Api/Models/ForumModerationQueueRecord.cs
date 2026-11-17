using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

public static class ForumModerationQueueStatuses
{
    /// <summary>高置信度违规，内容已转 pending_review，等待运营者裁决。</summary>
    public const string Pending = "pending";
    /// <summary>低置信度可疑，内容仍公开，等待运营者复核。</summary>
    public const string Flagged = "flagged";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class ForumModerationSources
{
    /// <summary>LLM 内容判定产出。</summary>
    public const string Llm = "llm";
    /// <summary>同作者窗口内重复内容检测产出（不走 LLM）。</summary>
    public const string Duplicate = "duplicate";
}

public static class ForumModerationVerdicts
{
    public const string Ok = "ok";
    public const string Spam = "spam";
    public const string Sexual = "sexual";
    public const string Political = "political";
    public const string Other = "other";

    public static readonly string[] Known = { Ok, Spam, Sexual, Political, Other };
}

/// <summary>
/// AI 版主阶段一复审队列。不复用 forum_reports：后者唯一索引以 ReporterSub 为键，
/// 无法承载无举报人的 AI 生成行。
/// </summary>
[BsonIgnoreExtraElements]
public class ForumModerationQueueRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";

    /// <summary>post or reply</summary>
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    /// <summary>帖 id；回复时指向所属帖，便于深链。</summary>
    public string PostId { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string AuthorSubId { get; set; } = "";

    /// <summary>见 <see cref="ForumModerationSources"/>。</summary>
    public string Source { get; set; } = ForumModerationSources.Llm;
    public string Status { get; set; } = ForumModerationQueueStatuses.Pending;

    /// <summary>执行判定的 LLM 档案 Id；重复内容检测来源为空。</summary>
    public string? LlmProfileId { get; set; }
    public string? Verdict { get; set; }
    public double? Confidence { get; set; }
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? HandledBySub { get; set; }
    public DateTime? HandledAtUtc { get; set; }
}

/// <summary>
/// 每日分类调用计数。独立集合而非从队列聚合：干净内容不入队，
/// 用队列行数当调用数会系统性低估成本，导致上限形同虚设。
/// </summary>
[BsonIgnoreExtraElements]
public class ForumModerationDailyCallRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";

    /// <summary>UTC 日期，格式 yyyy-MM-dd。</summary>
    public string Day { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>
/// 重复内容检测指纹。窗口内同作者同哈希即判定重复。
/// 只存哈希，不存正文，避免二次留存用户内容。
/// </summary>
[BsonIgnoreExtraElements]
public class ForumContentHashRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";

    public string AuthorSubId { get; set; } = "";
    public string Hash { get; set; } = "";
    /// <summary>post or reply</summary>
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
