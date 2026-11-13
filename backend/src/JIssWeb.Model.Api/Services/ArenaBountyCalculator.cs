using System.Globalization;
using JIssWeb.Model.Api.Options;

namespace JIssWeb.Model.Api.Services;

/// <summary>纯函数推导结果：Applicable=false 表示账号不适用（缺创建时间 / 周键非法），应跳过并记日志。</summary>
public readonly record struct ArenaBountyComputation(bool Applicable, int SurvivedWeeks, int Bounty);

/// <summary>
/// 悬赏值纯函数（#34 P0）。输入仅「账号创建时间 + weekKey + 配置」，输出幂等。
/// 绝不逐周 ×2 累加：ArenaRevealBackgroundService 启动会补跑最多 12 个不同 weekKey，
/// 累加写法一次重启就会放大 4096 倍且重跑不收敛。
/// weekKey 采用 #33 的 Asia/Shanghai 周一起算口径，weekEndUtc = 下周一 00:00 本地（UTC 前一日 16:00）。
/// </summary>
public static class ArenaBountyCalculator
{
    private static readonly DateTime MinValidCreatedAtUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ArenaBountyComputation Compute(DateTime createdAtUtc, string weekKey, ArenaBountyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(weekKey) ||
            !DateTime.TryParseExact(weekKey, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var mondayLocal))
            return new ArenaBountyComputation(false, 0, 0);

        var created = createdAtUtc.Kind == DateTimeKind.Utc
            ? createdAtUtc
            : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        if (created == default || created < MinValidCreatedAtUtc)
            return new ArenaBountyComputation(false, 0, 0);

        var weekEndUtc = new DateTimeOffset(mondayLocal.AddDays(7), TimeSpan.FromHours(8))
            .ToUniversalTime().UtcDateTime;

        var elapsed = weekEndUtc - created;
        if (elapsed < TimeSpan.FromDays(7))
            return new ArenaBountyComputation(true, 0, 0);

        var weeks = (int)Math.Floor(elapsed.TotalDays / 7d);
        if (weeks < 1)
            return new ArenaBountyComputation(true, 0, 0);

        return new ArenaBountyComputation(true, weeks, BountyForWeeks(weeks, options));
    }

    /// <summary>base × 2^(weeks-1) 收敛到 CapPoints；用累乘提前收敛，避免 int 溢出。</summary>
    public static int BountyForWeeks(int weeks, ArenaBountyOptions options)
    {
        if (weeks < 1 || options.BasePoints <= 0 || options.CapPoints <= 0)
            return 0;

        var points = options.BasePoints;
        for (var i = 1; i < weeks; i++)
        {
            if (points >= options.CapPoints || points > options.CapPoints / 2)
                return options.CapPoints;
            points *= 2;
        }

        return Math.Min(points, options.CapPoints);
    }
}
