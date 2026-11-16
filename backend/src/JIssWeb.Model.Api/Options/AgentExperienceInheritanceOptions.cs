namespace JIssWeb.Model.Api.Options;

/// <summary>
/// 图灵场「智能体经验继承（提示词注入阶段）」配置（#38）。
/// Enabled 默认关闭：关闭时零提取、零衰减、零注入，完全回归 #31/#32/#34 行为。
/// 只读展示（名人堂世代 / 仪表盘曲线）不依赖本开关。
/// </summary>
public sealed class AgentExperienceInheritanceOptions
{
    public const string SectionName = "AgentExperienceInheritance";

    /// <summary>总开关，默认关闭。</summary>
    public bool Enabled { get; set; }

    /// <summary>新建 Persona 时按 weight 降序继承的条目数上限。</summary>
    public int TopN { get; set; } = 5;

    /// <summary>存活天数归一化基准（天），默认 28 ≈ 4 周。effectivenessScore = clamp(survivalDays / NormalizeDays, 0, 1)。</summary>
    public int NormalizeDays { get; set; } = 28;

    /// <summary>每月经验权重衰减系数。</summary>
    public double DecayFactor { get; set; } = 0.85;

    /// <summary>近期被继承且来源高存活条目的额外奖励系数。</summary>
    public double SurvivalBonusFactor { get; set; } = 1.2;

    /// <summary>「近期被继承」判定的时间窗（天）。</summary>
    public int RecentInheritDays { get; set; } = 30;
}
