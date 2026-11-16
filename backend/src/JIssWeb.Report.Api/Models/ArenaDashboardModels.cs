namespace JIssWeb.Report.Api.Models;

/// <summary>
/// 运营仪表盘一次性返回的只读聚合视图模型（#36）。共 6 个面板（系统健康 / 经济 / 游戏 / 待处理 / 社区 / 世代存活曲线）。
/// 所有数据均由 ArenaDashboardService 从 jissweb_model 库只读聚合而来，不含任何写操作。
/// </summary>
public sealed class ArenaDashboardViewModel
{
    public DateTime GeneratedAtUtc { get; set; }
    public SystemHealthPanel SystemHealth { get; set; } = new();
    public EconomyPanel Economy { get; set; } = new();
    public GameplayPanel Gameplay { get; set; } = new();
    public QueuePanel Queue { get; set; } = new();
    public CommunityPanel Community { get; set; } = new();
    public GenerationSurvivalPanel GenerationSurvival { get; set; } = new();
}

/// <summary>#38 世代存活曲线：按 agent_personas.Generation 分组的只读聚合。</summary>
public sealed class GenerationSurvivalPanel
{
    public List<GenerationSurvivalPoint> Points { get; set; } = new();
}

public sealed class GenerationSurvivalPoint
{
    public int Generation { get; set; }
    public int Count { get; set; }
    public double AvgSurvivalDays { get; set; }
}

public sealed class SystemHealthPanel
{
    /// <summary>healthy | degraded。当且仅当存在 scheduler 异常时为 degraded。</summary>
    public string SchedulerStatus { get; set; } = "healthy";
    public DateTime? SchedulerLastTickAtUtc { get; set; }
    public List<LlmProfileHealth> LlmProfiles { get; set; } = new();
    public int AgentAliveCount { get; set; }
    public int AgentTotalCount { get; set; }
    public List<PendingRevealItem> PendingReveal { get; set; } = new();
}

public sealed class LlmProfileHealth
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string? LastProbe { get; set; } // ok | failed | null（null = 尚未探测）
}

public sealed class PendingRevealItem
{
    public string JobId { get; set; } = "";
    public string TargetPersonaId { get; set; } = "";
    public string State { get; set; } = "";
}

public sealed class EconomyPanel
{
    public double WeeklyNetFlow { get; set; }
    public double NetFlowRatioPct { get; set; }
    public string NetFlowLevel { get; set; } = "healthy"; // healthy | warning | danger
    public double PointsInjectionDensity { get; set; }
    public double LastWeekDensity { get; set; }
    public double Aiqi { get; set; }
    public bool AiqiConfident { get; set; }
}

public sealed class GameplayPanel
{
    public int WeeklyAccusations { get; set; }
    public DateTime NextRevealAtUtc { get; set; }
    public List<LegendaryPreyItem> LegendaryPrey { get; set; } = new();
    public List<TopDetectiveItem> TopDetectives { get; set; } = new();
}

public sealed class LegendaryPreyItem
{
    public string PersonaId { get; set; } = "";
    public int SurvivedWeeks { get; set; }
    public int CurrentBounty { get; set; }
}

public sealed class TopDetectiveItem
{
    public string UserId { get; set; } = "";
    public int Count { get; set; }
}

public sealed class QueuePanel
{
    public List<PendingReportItem> PendingReports { get; set; } = new();
    public List<AgentAlertItem> AgentAlerts { get; set; } = new();
}

public sealed class PendingReportItem
{
    public string ReportId { get; set; } = "";
    public string? Reason { get; set; }
}

public sealed class AgentAlertItem
{
    public string PersonaId { get; set; } = "";
    public string Type { get; set; } = ""; // error | timeout
    public string? Detail { get; set; }
}

public sealed class CommunityPanel
{
    public int WeeklyActiveHumans { get; set; }
    public int WeeklyPostsTotal { get; set; }
    public int WeeklyPostsAi { get; set; }
    public int WeeklyPostsHuman { get; set; }
}
