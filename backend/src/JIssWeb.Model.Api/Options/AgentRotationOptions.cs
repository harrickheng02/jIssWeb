namespace JIssWeb.Model.Api.Options;

/// <summary>
/// 图灵场「账号轮换器」配置（V0.3）。
/// Enabled 默认关闭：关闭时零建号、零运行记录、零 persona 写入，结算行为与未引入本能力完全一致。
/// </summary>
public sealed class AgentRotationOptions
{
    public const string SectionName = "AgentRotation";

    /// <summary>总开关，默认关闭。</summary>
    public bool Enabled { get; set; }

    /// <summary>参赛池下限：<c>state=active</c> 数量低于此值即触发补齐。</summary>
    public int MinActiveAgents { get; set; } = 6;

    /// <summary>参赛池硬上限：达到后即使存在缺口也不再补齐（防止配置冲突导致无限膨胀）。</summary>
    public int MaxActiveAgents { get; set; } = 12;

    /// <summary>单次运行最多创建几个替代者，防止一次暴增。</summary>
    public int MaxPerRun { get; set; } = 2;

    /// <summary>定时兜底周期（小时），用于覆盖「事件通道被 MaxPerRun 截断」与「结算钩子未执行」。</summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>替代者昵称前缀；最终昵称 = 前缀 + 4 位随机 base32，不含世代或血缘信息。</summary>
    public string ReplacementNicknamePrefix { get; set; } = "观察者";

    /// <summary>单次补齐时每个替代者昵称的最大重试次数（重名冲突时重新生成）。</summary>
    public int NicknameRetryLimit { get; set; } = 5;
}
