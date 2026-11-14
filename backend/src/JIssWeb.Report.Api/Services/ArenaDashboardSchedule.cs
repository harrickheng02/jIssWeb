using System;
using System.Globalization;

namespace JIssWeb.Report.Api.Services;

/// <summary>
/// 运营仪表盘所需的周键 / 揭露倒计时 / 时区解析工具（#36）。
/// 这些纯函数与 JIssWeb.Model.Api 内的同名实现保持口径一致（缺 tzdata 降级固定 +8），
/// 但本服务不引用 Model.Api 程序集（避免 publish 文件冲突与业务耦合），因此在此自包含实现。
/// </summary>
public sealed class ArenaDashboardRevealOptions
{
    public int RevealHourLocal { get; set; } = 22;
    public string TimeZoneId { get; set; } = "Asia/Shanghai";
}

public static class ArenaDashboardSchedule
{
    /// <summary>下一触发点的 UTC 时间：本周五（若今日周五且未到时刻则今天，否则下周五）RevealHourLocal 本地时刻。</summary>
    public static DateTimeOffset ComputeNextRunUtc(DateTimeOffset nowUtc, int revealHourLocal, string timeZoneId)
    {
        var tz = Resolve(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var todayRevealLocal = new DateTime(
            localNow.Year, localNow.Month, localNow.Day, revealHourLocal, 0, 0, DateTimeKind.Unspecified);

        var daysToFriday = (((int)DayOfWeek.Friday - (int)localNow.DayOfWeek) + 7) % 7;
        DateTime nextFridayLocal;
        if (daysToFriday == 0)
        {
            nextFridayLocal = localNow.TimeOfDay < new TimeSpan(revealHourLocal, 0, 0)
                ? todayRevealLocal
                : todayRevealLocal.AddDays(7);
        }
        else
        {
            nextFridayLocal = todayRevealLocal.AddDays(daysToFriday);
        }

        var offset = new DateTimeOffset(nextFridayLocal, tz.GetUtcOffset(nextFridayLocal));
        return offset.ToUniversalTime();
    }

    /// <summary>当前自然周键（周一为周起点，格式 yyyy-MM-dd）。与 Model.Api 的 ArenaWeekKeys.For 口径一致。</summary>
    public static string For(DateTimeOffset now)
    {
        var tz = Resolve("Asia/Shanghai");
        var local = TimeZoneInfo.ConvertTime(now, tz).Date;
        var dow = (int)local.DayOfWeek; // 周日=0
        var diff = (7 + dow - 1) % 7;
        var monday = local.AddDays(-diff);
        return monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>时区解析（缺 tzdata 时固定 UTC+8，与既有降级逻辑一致）。</summary>
    public static TimeZoneInfo Resolve(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        }
    }
}
