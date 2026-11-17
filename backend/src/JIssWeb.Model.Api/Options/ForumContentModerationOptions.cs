namespace JIssWeb.Model.Api.Options;

/// <summary>
/// 图灵场 V0.3「AI 版主阶段一」配置。
/// Enabled 默认关闭：关闭时零 LLM 调用、零哈希写入、零队列写入、零内容状态改写。
/// 三条铁律：屏蔽词与命中结果不进 prompt；只有运营者能最终处置；判定失败一律 fail-open。
/// </summary>
public sealed class ForumContentModerationOptions
{
    public const string SectionName = "Forum:ContentModeration";

    /// <summary>总开关，默认关闭。</summary>
    public bool Enabled { get; set; }

    /// <summary>置信度达到此值即入队标记（内容仍公开）。</summary>
    public double FlagConfidence { get; set; } = 0.6;

    /// <summary>置信度达到此值且 verdict 命中 <see cref="AutoHideVerdicts"/> 时转入待审。</summary>
    public double AutoHideConfidence { get; set; } = 0.9;

    /// <summary>允许自动隐藏的判定类别白名单；<c>ok</c> 永不在此列。</summary>
    public List<string> AutoHideVerdicts { get; set; } = new() { "spam", "sexual", "political" };

    /// <summary>单次判定超时（毫秒）。超时按 fail-open 放行。</summary>
    public int TimeoutMs { get; set; } = 2500;

    /// <summary>每日 LLM 判定调用上限，超出后一律放行。</summary>
    public int DailyLlmCallCap { get; set; } = 500;

    /// <summary>重复内容检测的时间窗（小时）。</summary>
    public int DuplicateWindowHours { get; set; } = 24;

    /// <summary>用于内容判定的 LLM 档案 Id；为空时回退到默认可用档案。</summary>
    public string? LlmProfileId { get; set; }
}
