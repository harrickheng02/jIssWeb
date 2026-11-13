using System.Globalization;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场周键工具。以「Asia/Shanghai 本地时间周一」为周起点，格式化为 yyyy-MM-dd（周一日期）。
/// 缺 tzdata 时降级固定 UTC+8。所有写路径与结算路径共用同一算法，保证一致。
/// </summary>
public static class ArenaWeekKeys
{
    public static string For(DateTimeOffset now)
    {
        var tz = ResolveTimeZone();
        var local = TimeZoneInfo.ConvertTime(now, tz).Date;
        // 周一为周起点：diff = (7 + dow - 1) % 7。
        var dow = (int)local.DayOfWeek; // 周日=0
        var diff = (7 + dow - 1) % 7;
        var monday = local.AddDays(-diff);
        return monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo ResolveTimeZone()
        => ArenaTimeZones.Resolve("Asia/Shanghai");
}
