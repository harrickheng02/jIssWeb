namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 悬赏能力（#34）对结算管线的三个挂接点。所有方法在 Enabled=false 时 SHALL 零写入并回落到 #33 基线。
/// 依赖方向保持单向：本服务只落在 Model.Api，SHALL NOT 调用 Accounting.Api、SHALL NOT 写 user_points_ledger。
/// </summary>
public interface IArenaBountyService
{
    /// <summary>单周总投放上限（第二层封顶，仅用于运营告警，不削减金额）。</summary>
    int MaxPayoutPerWeek { get; }

    /// <summary>
    /// 在结算批次原子领取成功后、prepare 之前调用：按账号 × weekKey 幂等计算悬赏状态，
    /// 并产出奖章聚合公告 / 幸存者标记等一次性事件。返回本周处理的账号数。
    /// </summary>
    Task<int> ComputeWeekAsync(string weekKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// commit 阶段读取某目标在当前 weekKey 的悬赏值。灰度关闭 / 状态缺失 / 计算失败时 SHALL 恰好返回 50。
    /// </summary>
    Task<int> GetBountyAsync(string targetUserId, string weekKey, CancellationToken cancellationToken = default);

    /// <summary>成功指认传说级猎物时授予图灵猎手徽章（11000 幂等）。非传说级目标 SHALL NOT 授予。</summary>
    Task RecordHunterBadgeAsync(
        string userId,
        string targetUserId,
        string weekKey,
        int bountyPoints,
        CancellationToken cancellationToken = default);
}
