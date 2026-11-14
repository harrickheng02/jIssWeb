using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Report.Api.Models;

/// <summary>
/// 运营仪表盘只读聚合使用的轻量 Bson 模型（仅取所需字段）。
/// netflow / aiqi / points 等模型定义在 Accounting.Api 程序集，本服务不引用它，
/// 因此在本地定义最小只读投影，避免跨业务程序集耦合。
/// 源集合（forum_reports 等）含远超本投影的字段，统一标记 BsonIgnoreExtraElements，
/// 避免因后续集合加字段而反序列化失败（只读聚合必须稳健）。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardAgentPersona
{
    [BsonId] public string Id { get; set; } = "";
    public string PersonaId { get; set; } = "";
    public string AgentUserId { get; set; } = "";
    public string State { get; set; } = "";
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardSchedulerState
{
    [BsonId] public string Id { get; set; } = "";
    public string PersonaId { get; set; } = "";
    public string AgentUserId { get; set; } = "";
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardLlmProfile
{
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardRevealJob
{
    [BsonId] public string Id { get; set; } = "";
    public string State { get; set; } = "";
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardNetflowResult
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = "";
    public string WeekKey { get; set; } = "";
    public int NetFlow { get; set; }
    public bool InflationAlert { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardAiqiResult
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = "";
    public string WeekKey { get; set; } = "";
    public double Aiqi { get; set; }
    public bool SampleSufficient { get; set; }
    public int ActiveUsers { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardPointsLedger
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = "";
    public int Balance { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardPointsTransaction
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = "";
    public int Delta { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardAccusation
{
    [BsonId] public string Id { get; set; } = "";
    public string WeekKey { get; set; } = "";
    public string AccuserId { get; set; } = "";
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardBountyState
{
    [BsonId] public string Id { get; set; } = "";
    public string AgentUserId { get; set; } = "";
    public int SurvivedWeeks { get; set; }
    public int CurrentBounty { get; set; }
    public bool IsLegendaryPrey { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardForumPost
{
    [BsonId] public string Id { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardForumReply
{
    [BsonId] public string Id { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "反序列化目标")]
[BsonIgnoreExtraElements]
internal sealed class DashboardForumReport
{
    [BsonId] public string Id { get; set; } = "";
    public string? Reason { get; set; }
    public string Status { get; set; } = "";
}

internal static class DashboardCollectionNames
{
    public const string AgentPersonas = "agent_personas";
    public const string SchedulerStates = "agent_scheduler_state";
    public const string LlmProfiles = "agent_llm_profiles";
    public const string RevealJobs = "arena_reveal_jobs";
    public const string NetflowResults = "arena_netflow_results";
    public const string AiqiResults = "arena_aiqi_results";
    public const string PointsLedger = "user_points_ledger";
    public const string PointsTransactions = "user_points_transactions";
    public const string Accusations = "arena_accusations";
    public const string BountyStates = "arena_bounty_states";
    public const string ForumPosts = "forum_posts";
    public const string ForumReplies = "forum_replies";
    public const string ForumReports = "forum_reports";
}
