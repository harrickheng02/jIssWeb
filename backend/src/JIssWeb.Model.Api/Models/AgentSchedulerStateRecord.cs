using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

[BsonIgnoreExtraElements]
public sealed class AgentSchedulerStateRecord
{
    /// <summary>Same as agentUserId / persona document _id.</summary>
    [BsonId]
    public string Id { get; set; } = "";

    public string PersonaId { get; set; } = "";
    public string AgentUserId { get; set; } = "";

    public bool Paused { get; set; }
    public bool AllowAutoPost { get; set; } = true;
    public bool AllowAutoReply { get; set; } = true;

    public int? MaxPostsPerDay { get; set; }
    public int? MaxRepliesPerDay { get; set; }
    public int? MinIntervalMinutes { get; set; }
    public int? JitterMinutes { get; set; }

    public DateTime? NextDueAtUtc { get; set; }
    public string QuotaDateUtc { get; set; } = "";
    public int PostsUsed { get; set; }
    public int RepliesUsed { get; set; }

    public DateTime? LastErrorAtUtc { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class AgentRuntimeConfigRecord
{
    public const string SingletonId = "default";

    [BsonId]
    public string Id { get; set; } = SingletonId;

    public bool? SchedulerEnabled { get; set; }
    public string? DeepSeekApiKey { get; set; }
    public string? DeepSeekBaseUrl { get; set; }
    public string? DeepSeekModel { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
