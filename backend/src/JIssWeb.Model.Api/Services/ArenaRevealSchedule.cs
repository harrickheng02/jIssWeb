using System;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场揭露调度纯函数（可单测，无 DB / 时钟依赖）。
/// 以「周五 RevealHourLocal（本地 Asia/Shanghai，缺 tzdata 降级 +8）」为每周触发点。
/// 触发时揭露的是「上一自然周」（周一为周起点，即 now 回退 7 天），
/// 保证当周（含周六 / 周日）指认都能在下一个周五被结算，且周一指认能在下周五落账（D3 用例 22 P0）。
/// </summary>
public static class ArenaRevealSchedule
{
    /// <summary>下一触发点的 UTC 时间：本周五（若今日周五且未到时刻则今天，否则下周五）RevealHourLocal 本地时刻。</summary>
    public static DateTimeOffset ComputeNextRunUtc(DateTimeOffset nowUtc, int revealHourLocal, string timeZoneId)
    {
        var tz = ArenaTimeZones.Resolve(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var todayRevealLocal = new DateTime(
            localNow.Year, localNow.Month, localNow.Day, revealHourLocal, 0, 0, DateTimeKind.Unspecified);

        DateTime nextFridayLocal;
        var daysToFriday = (((int)DayOfWeek.Friday - (int)localNow.DayOfWeek) + 7) % 7;
        if (daysToFriday == 0)
        {
            // 今天就是周五：本地时刻未到 RevealHourLocal 则今天，否则下周五。
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

    /// <summary>给定 nowUtc，揭露目标周键 = 本地 now 回退 daysBack 天所在自然周的周一。</summary>
    public static string WeekKeyForLocalDate(DateTimeOffset nowUtc, int daysBack, string timeZoneId)
    {
        var tz = ArenaTimeZones.Resolve(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var targetLocal = localNow.AddDays(daysBack);
        var targetOffset = new DateTimeOffset(
            targetLocal.Year, targetLocal.Month, targetLocal.Day, 0, 0, 0,
            tz.GetUtcOffset(targetLocal));
        return ArenaWeekKeys.For(targetOffset);
    }

    /// <summary>揭露目标周键（上一自然周）。</summary>
    public static string ComputeRevealWeekKey(DateTimeOffset nowUtc, string timeZoneId)
        => WeekKeyForLocalDate(nowUtc, -7, timeZoneId);
}
