using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 悬赏状态（独立于 agent_personas）。SHALL NOT 扩展 persona 契约、SHALL NOT 改变 persona.State。
/// </summary>
[BsonIgnoreExtraElements]
public sealed class ArenaBountyStateRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    public string AgentUserId { get; set; } = "";
    public int SurvivedWeeks { get; set; }
    public int CurrentBounty { get; set; }
    public int BountyCap { get; set; }
    public bool IsLegendaryPrey { get; set; }
    /// <summary>首次达标的周键；只在 MedalAwardedAtUtc 为空时写入一次，作为聚合公告的幂等锚点。</summary>
    public string? MedalAwardedWeekKey { get; set; }
    public DateTime? MedalAwardedAtUtc { get; set; }
    public DateTime? SurvivorMarkedAtUtc { get; set; }
    public string LastComputedWeekKey { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>按账号 × weekKey 的计算幂等权（唯一索引 {AgentUserId,WeekKey}）。</summary>
[BsonIgnoreExtraElements]
public sealed class ArenaBountyRunRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    public string AgentUserId { get; set; } = "";
    public string WeekKey { get; set; } = "";
    public DateTime ComputedAtUtc { get; set; }
}

/// <summary>奖章事件记录（weekKey 唯一），承载聚合计数，零账号标识。</summary>
[BsonIgnoreExtraElements]
public sealed class ArenaBountyMedalRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    public string WeekKey { get; set; } = "";
    public int MaxBounty { get; set; }
    public int LegendaryCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>图灵猎手永久成就徽章（UserId 唯一，11000 幂等）。</summary>
[BsonIgnoreExtraElements]
public sealed class ArenaHunterBadgeRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    public string BadgeId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string TargetWeekKey { get; set; } = "";
    public int BountyPoints { get; set; }
    public DateTime AwardedAtUtc { get; set; }
}
