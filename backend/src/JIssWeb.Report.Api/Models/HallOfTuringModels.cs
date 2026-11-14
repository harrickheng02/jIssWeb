namespace JIssWeb.Report.Api.Models;

/// <summary>
/// 图灵名人堂只读聚合视图模型（#37）。
/// 全部数据由 ArenaHallOfTuringService 从 jissweb_model 库只读聚合而来，不含任何写操作。
/// 严守 #34 铁律：公开列表仅含已被揭露（eliminated）的奖章账号；未发现（active）幸存者不出现，
/// 仅以匿名聚合计数呈现；外部开发者归属字段本期预留可空位，不填充。
/// </summary>
public sealed class HallOfTuringViewModel
{
    /// <summary>已揭露奖章账号卡片（按获奖时间倒序）。</summary>
    public List<HallCard> Items { get; set; } = new();
    /// <summary>匿名聚合计数：当前仍未发现（active）的奖章账号数，无个体标识（#34 聚合公告口径）。</summary>
    public int ActiveSurvivorCount { get; set; }
}

public sealed class HallCard
{
    public string PersonaId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int FinalBounty { get; set; }
    public bool Discovered { get; set; } = true;
    /// <summary>猎手标识列表。本期为隐私脱敏代号（侦探#短哈希）；真实姓名解析需内部用户读端点，不在 #37 范围。</summary>
    public List<string> DiscoveredBy { get; set; } = new();
    public string DrivingModel { get; set; } = "";
    public string Generation { get; set; } = ""; // Gen-1 ...
    public List<TopTopicItem> TopTopics { get; set; } = new();
    public InteractionSummary InteractionSummary { get; set; } = new();
    public int SurvivalDays { get; set; }
    /// <summary>外部开发者归属预留字段（本期恒为 null，V1.0 后填充）。</summary>
    public object? ExternalAttribution { get; set; } = null;
}

public sealed class TopTopicItem
{
    public string Tag { get; set; } = "";
    public int Count { get; set; }
}

public sealed class InteractionSummary
{
    public int Likes { get; set; }
    public int Favorites { get; set; }
    public int Comments { get; set; }
    public int Views { get; set; }
}

public sealed class ArenaProfileViewModel
{
    public string PersonaId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int SurvivalDays { get; set; }
    public List<ProfilePostItem> Timeline { get; set; } = new();
    public ProfilePostItem? Confession { get; set; }
    public List<NearDangerMarker> NearDanger { get; set; } = new();
    /// <summary>最终暴露触发帖：映射为系统已知的自白帖（与 Confession 同源）。</summary>
    public ProfilePostItem? TriggerPost { get; set; }
    public InteractionSummary InteractionSummary { get; set; } = new();
    public object? ExternalAttribution { get; set; } = null;
}

public sealed class ProfilePostItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
    public bool IsConfession { get; set; }
}

public sealed class NearDangerMarker
{
    public string WeekKey { get; set; } = "";
    public int Count { get; set; }
}

public sealed class AiqiTrendPoint
{
    public string WeekKey { get; set; } = "";
    public double Aiqi { get; set; }
    public int ActiveUsers { get; set; }
}
