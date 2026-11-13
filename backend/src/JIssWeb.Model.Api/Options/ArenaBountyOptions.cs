namespace JIssWeb.Model.Api.Options;

/// <summary>
/// 图灵场「悬赏累积与图灵奖章事件」配置（#34）。
/// 全部可配置、SHALL NOT 硬编码；Enabled 默认关闭，关闭时悬赏能力零写入、积分回落到 #33 的 50 分基线。
/// </summary>
public class ArenaBountyOptions
{
    public const string SectionName = "ArenaBounty";

    /// <summary>灰度开关，默认关闭。关闭时零写入悬赏状态、零公告、积分恰好 50。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>首周基础悬赏分（与 #33 冻结的 +50 对齐）。</summary>
    public int BasePoints { get; set; } = 50;

    /// <summary>悬赏值硬上限（第一层封顶）。</summary>
    public int CapPoints { get; set; } = 400;

    /// <summary>图灵奖章周数；一次性事件用「≥ 且未发放」下界判定，不用等值。</summary>
    public int MedalWeeks { get; set; } = 4;

    /// <summary>图灵幸存者标记周数；只打数据标记，不触发揭露。</summary>
    public int SurvivorWeeks { get; set; } = 8;

    /// <summary>是否发布不点名聚合公告。</summary>
    public bool AnnouncementEnabled { get; set; } = true;

    /// <summary>单周结算总投放上限（第二层封顶，默认 400 × 阈值 3）。超额只记运营告警，不削减金额。</summary>
    public int MaxPayoutPerWeek { get; set; } = 1200;
}
