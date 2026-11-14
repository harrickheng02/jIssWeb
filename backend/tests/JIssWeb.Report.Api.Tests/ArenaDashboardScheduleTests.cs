using System;
using JIssWeb.Report.Api.Services;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// 运营仪表盘调度纯函数单测（无 DB / 无时钟外部依赖）。
/// 与 Model.Api 的 ArenaRevealSchedule 口径一致：Asia/Shanghai ≡ UTC+8。
/// </summary>
public class ArenaDashboardScheduleTests
{
    private const string Tz = "Asia/Shanghai";

    [Fact]
    public void ComputeNextRunUtc_周三本地下午_返回本周五22点本地对应UTC()
    {
        // 2026-06-10 周三本地 14:00 → UTC 06:00Z；下一个周五 22:00 本地 = UTC 14:00Z。
        var nowUtc = DateTimeOffset.Parse("2026-06-10T06:00:00Z");
        var next = ArenaDashboardSchedule.ComputeNextRunUtc(nowUtc, 22, Tz);
        Assert.Equal(DateTimeOffset.Parse("2026-06-12T14:00:00Z"), next);
        Assert.Equal(DayOfWeek.Friday, TimeZoneInfo.ConvertTime(next, ArenaDashboardSchedule.Resolve(Tz)).DayOfWeek);
    }

    [Fact]
    public void ComputeNextRunUtc_周五本地未到点_返回当日22点本地对应UTC()
    {
        // 2026-06-12 周五本地 18:00（未到 22:00）→ UTC 10:00Z；当日 22:00 本地 = UTC 14:00Z。
        var nowUtc = DateTimeOffset.Parse("2026-06-12T10:00:00Z");
        var next = ArenaDashboardSchedule.ComputeNextRunUtc(nowUtc, 22, Tz);
        Assert.Equal(DateTimeOffset.Parse("2026-06-12T14:00:00Z"), next);
    }

    [Fact]
    public void ComputeNextRunUtc_周五本地过点_返回下周五22点本地对应UTC()
    {
        // 2026-06-12 周五本地 23:00（已过 22:00）→ UTC 15:00Z；下周五 2026-06-19 22:00 本地 = UTC 14:00Z。
        var nowUtc = DateTimeOffset.Parse("2026-06-12T15:00:00Z");
        var next = ArenaDashboardSchedule.ComputeNextRunUtc(nowUtc, 22, Tz);
        Assert.Equal(DateTimeOffset.Parse("2026-06-19T14:00:00Z"), next);
    }

    [Fact]
    public void For_周三_返回当周周一()
    {
        // 2026-06-10 周三（UTC 中午，本地同日 20:00）→ 周一 2026-06-08。
        var nowUtc = DateTimeOffset.Parse("2026-06-10T12:00:00Z");
        Assert.Equal("2026-06-08", ArenaDashboardSchedule.For(nowUtc));
    }

    [Fact]
    public void For_周日_返回当周周一()
    {
        // 2026-06-14 周日 → 周一 2026-06-08。
        var nowUtc = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        Assert.Equal("2026-06-08", ArenaDashboardSchedule.For(nowUtc));
    }

    [Fact]
    public void For_周一_返回自身()
    {
        // 2026-06-15 周一 → 2026-06-15。
        var nowUtc = DateTimeOffset.Parse("2026-06-15T12:00:00Z");
        Assert.Equal("2026-06-15", ArenaDashboardSchedule.For(nowUtc));
    }

    [Fact]
    public void Resolve_缺tzdata时_降级固定UTC加8()
    {
        // 一个几乎不存在的时区 id 触发降级分支，返回 UTC+8。
        var tz = ArenaDashboardSchedule.Resolve("This/TimeZone/Does/Not/Exist");
        Assert.Equal(TimeSpan.FromHours(8), tz.BaseUtcOffset);
    }
}
