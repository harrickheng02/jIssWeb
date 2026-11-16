using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Report.Api.Models;

/// <summary>
/// 图灵名人堂（#37）只读聚合使用的轻量 Bson 投影模型。
/// 与 #36 同范式：源集合含远超投影的字段，统一标记 BsonIgnoreExtraElements 以稳健反序列化；
/// Accounting 的 arena_aiqi_results 的 _id 为原生 ObjectId，对应 Id 加 BsonRepresentation(ObjectId)。
/// 源集合（arena_points_events / arena_target_tallies / arena_reveal_jobs）的 _id 为字符串，无需特殊处理。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallBountyState
{
    [BsonId] public string Id { get; set; } = "";
    public string AgentUserId { get; set; } = "";
    public int CurrentBounty { get; set; }
    public int BountyCap { get; set; }
    public DateTime? MedalAwardedAtUtc { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallAgentPersona
{
    [BsonId] public string Id { get; set; } = "";
    public string PersonaId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string AgentUserId { get; set; } = "";
    public string Model { get; set; } = "";
    public string? LlmProfileId { get; set; }
    public int Generation { get; set; } = 1;
    public List<string> InheritedFrom { get; set; } = new();
    public int SurvivalDays { get; set; }
    public string State { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallLlmProfile
{
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallForumPost
{
    [BsonId] public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public int LikeCount { get; set; }
    public int FavoriteCount { get; set; }
    public int CommentCount { get; set; }
    public int ViewCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string State { get; set; } = "";
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallForumReply
{
    [BsonId] public string Id { get; set; } = "";
    public string PostId { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string State { get; set; } = "";
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallRevealJob
{
    [BsonId] public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public List<string> ConfessionPostIds { get; set; } = new();
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallTargetTally
{
    [BsonId] public string Id { get; set; } = "";
    public string WeekKey { get; set; } = "";
    public string TargetUserId { get; set; } = "";
    public int Count { get; set; }
    public string Status { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallPointsEvent
{
    [BsonId] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "";
    public string TargetUserId { get; set; } = "";
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class HallAiqiResult
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = "";
    public string WeekKey { get; set; } = "";
    public double Aiqi { get; set; }
    public int ActiveUsers { get; set; }
}

internal static class HallCollectionNames
{
    public const string AgentPersonas = "agent_personas";
    public const string LlmProfiles = "agent_llm_profiles";
    public const string BountyStates = "arena_bounty_states";
    public const string ForumPosts = "forum_posts";
    public const string ForumReplies = "forum_replies";
    public const string RevealJobs = "arena_reveal_jobs";
    public const string TargetTallies = "arena_target_tallies";
    public const string PointsEvents = "arena_points_events";
    public const string AiqiResults = "arena_aiqi_results";
}
