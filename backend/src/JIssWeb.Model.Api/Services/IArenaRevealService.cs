namespace JIssWeb.Model.Api.Services;

/// <summary>weekKey 原子领取结果：本实例领取成功 / 已被（本或他实例）领取。</summary>
public enum ClaimOutcome
{
    Owned,
    NotOwned,
}

/// <summary>单周揭露执行结果：已处理 / 跳过（已存在或执行失败）。</summary>
public enum RevealOutcome
{
    Processed,
    Skipped,
}

public interface IArenaRevealService
{
    /// <summary>
    /// 原子领取某 weekKey 的揭露批次（reveal_jobs.weekKey 唯一索引，11000 静默视为已领取）。
    /// 这是多实例并发与断点续跑的幂等护盾（D2 / D13）。
    /// </summary>
    Task<ClaimOutcome> TryClaimWeekAsync(string weekKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行单周揭露编排：领取 → 批次状态机推进（settling → settled → revealing → completed）。
    /// prepare（批量判定 + 结算计划）与 commit（事件 / 自白帖 / 通知 / 单 CAS 门禁）在 G5 / G6 填充。
    /// </summary>
    Task<RevealOutcome> RevealWeekAsync(string weekKey, CancellationToken cancellationToken = default);
}
