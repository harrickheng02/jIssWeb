namespace JIssWeb.Model.Api.Options;

/// <summary>
/// 图灵场「静默指认与每周揭露」配置。仅承载写逻辑所需的本地配置（阈值、时区、归档天数等），
/// 不通过 .env 传递密钥；LLM 配置复用 agent_llm_profiles 多档案。
/// </summary>
public class ArenaRevealOptions
{
    public const string SectionName = "ArenaReveal";

    /// <summary>揭露时刻（本地时区小时，默认 22:00）。</summary>
    public int RevealHourLocal { get; set; } = 22;

    /// <summary>本地时区 ID；缺 tzdata 时降级固定 +8。</summary>
    public string TimeZoneId { get; set; } = "Asia/Shanghai";

    /// <summary>揭露阈值（人数），可配置，本期不建后台 UI。</summary>
    public int Threshold { get; set; } = 3;

    /// <summary>结算后归档保留天数（实现取 ≥14，默认 21 留余量）。</summary>
    public int ArchiveRetentionDays { get; set; } = 21;

    /// <summary>服务启动是否补跑错过的 weekKey。</summary>
    public bool CatchUpEnabled { get; set; } = true;

    /// <summary>后台揭露服务是否启用（默认关闭，便于测试与灰度）。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>自白帖生成绑定的 LLM 档案 ID（可空，缺省时使用首个启用档案）。</summary>
    public string? LlmProfileId { get; set; }
}
