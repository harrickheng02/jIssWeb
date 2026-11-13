using System;
using JIssWeb.Model.Api.Services;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 图灵场揭露调度纯函数单测（无 DB / 无时钟依赖）。
/// 时区解析在缺 tzdata 时降级 +8，故以「Asia/Shanghai ≡ UTC+8」推导期望 UTC。
/// </summary>
public class ArenaRevealScheduleTests
{
    private const string Tz = "Asia/Shanghai";

    [Fact]
    public void ComputeNextRunUtc_周三本地下午_返回本周五22点本地对应UTC()
    {
        // 2026-06-10 是周三；本地 14:00 → UTC 06:00Z。下一个周五 22:00 本地 = UTC 14:00Z。
        var nowUtc = DateTimeOffset.Parse("2026-06-10T06:00:00Z");
        var next = ArenaRevealSchedule.ComputeNextRunUtc(nowUtc, 22, Tz);
        Assert.Equal(DateTimeOffset.Parse("2026-06-12T14:00:00Z"), next);
        Assert.Equal(DayOfWeek.Friday, TimeZoneInfo.ConvertTime(next, ArenaTimeZones.Resolve(Tz)).DayOfWeek);
    }

    [Fact]
    public void ComputeNextRunUtc_周五本地未到点_返回当日22点本地对应UTC()
    {
        // 2026-06-12 周五本地 18:00（未到 22:00）→ UTC 10:00Z。当日 22:00 本地 = UTC 14:00Z。
        var nowUtc = DateTimeOffset.Parse("2026-06-12T10:00:00Z");
        var next = ArenaRevealSchedule.ComputeNextRunUtc(nowUtc, 22, Tz);
        Assert.Equal(DateTimeOffset.Parse("2026-06-12T14:00:00Z"), next);
    }

    [Fact]
    public void ComputeNextRunUtc_周五本地过点_返回下周五22点本地对应UTC()
    {
        // 2026-06-12 周五本地 23:00（已过 22:00）→ UTC 15:00Z。下周五 2026-06-19 22:00 本地 = UTC 14:00Z。
        var nowUtc = DateTimeOffset.Parse("2026-06-12T15:00:00Z");
        var next = ArenaRevealSchedule.ComputeNextRunUtc(nowUtc, 22, Tz);
        Assert.Equal(DateTimeOffset.Parse("2026-06-19T14:00:00Z"), next);
    }

    [Fact]
    public void ComputeRevealWeekKey_揭露目标为上一自然周()
    {
        // 2026-06-12 周五本地 22:00 → UTC 14:00Z；揭露上一自然周 → 周一 2026-06-01。
        var nowUtc = DateTimeOffset.Parse("2026-06-12T14:00:00Z");
        var weekKey = ArenaRevealSchedule.ComputeRevealWeekKey(nowUtc, Tz);
        Assert.Equal("2026-06-01", weekKey);
        // 关系不变式：揭露周键 = 当周键回退 7 天。
        var currentWeek = ArenaWeekKeys.For(nowUtc);
        Assert.Equal(currentWeek, AddDays(weekKey, 7));
    }

    private static string AddDays(string yyyymmdd, int days)
    {
        var d = DateTime.ParseExact(yyyymmdd, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return d.AddDays(days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
