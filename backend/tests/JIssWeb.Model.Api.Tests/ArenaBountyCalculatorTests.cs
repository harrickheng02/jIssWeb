using System.Globalization;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// #34 悬赏纯函数单测（A1/A2/A3/A5/A10/A16）。
/// 无 DB / 时钟依赖；weekKey 采用 #33 的 Asia/Shanghai 周一起算口径。
/// </summary>
public class ArenaBountyCalculatorTests
{
    private const string WeekKey = "2026-07-06"; // 周一

    private static readonly ArenaBountyOptions Options = new();

    private static DateTime WeekEndUtc => new DateTimeOffset(
        DateTime.ParseExact(WeekKey, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(7),
        TimeSpan.FromHours(8)).ToUniversalTime().UtcDateTime;

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 100)]
    [InlineData(3, 200)]
    [InlineData(4, 400)]
    [InlineData(5, 400)]
    [InlineData(8, 400)]
    [InlineData(12, 400)]
    public void Compute_按存活周数翻倍并在上限收敛(int weeks, int expectedBounty)
    {
        var created = WeekEndUtc.AddDays(-7 * weeks);
        var result = ArenaBountyCalculator.Compute(created, WeekKey, Options);

        Assert.True(result.Applicable);
        Assert.Equal(weeks, result.SurvivedWeeks);
        Assert.Equal(expectedBounty, result.Bounty);
    }

    [Fact]
    public void Compute_存活不足7天_返回零周零悬赏()
    {
        var created = WeekEndUtc.AddDays(-3);
        var result = ArenaBountyCalculator.Compute(created, WeekKey, Options);

        Assert.True(result.Applicable);
        Assert.Equal(0, result.SurvivedWeeks);
        Assert.Equal(0, result.Bounty);
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("1999-12-31")]
    public void Compute_缺失或过早的CreatedAtUtc_判定不适用(string createdText)
    {
        var created = DateTime.ParseExact(createdText, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var result = ArenaBountyCalculator.Compute(created, WeekKey, Options);

        Assert.False(result.Applicable);
        Assert.Equal(0, result.Bounty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-week")]
    [InlineData("2026/07/06")]
    public void Compute_非法weekKey_判定不适用(string weekKey)
    {
        var result = ArenaBountyCalculator.Compute(WeekEndUtc.AddDays(-28), weekKey, Options);
        Assert.False(result.Applicable);
    }

    [Fact]
    public void Compute_纯函数幂等_重复调用结果逐次一致()
    {
        var created = WeekEndUtc.AddDays(-28);
        var first = ArenaBountyCalculator.Compute(created, WeekKey, Options);
        for (var i = 0; i < 20; i++)
            Assert.Equal(first, ArenaBountyCalculator.Compute(created, WeekKey, Options));
    }

    [Fact]
    public void Compute_补跑12个不同weekKey_结果由纯函数决定而非累加放大()
    {
        var created = WeekEndUtc.AddDays(-7);
        var monday = DateTime.ParseExact(WeekKey, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        for (var i = 0; i < 12; i++)
        {
            var wk = monday.AddDays(7 * i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var result = ArenaBountyCalculator.Compute(created, wk, Options);
            Assert.True(result.Applicable);
            Assert.Equal(i + 1, result.SurvivedWeeks);
            Assert.Equal(ArenaBountyCalculator.BountyForWeeks(i + 1, Options), result.Bounty);
            Assert.True(result.Bounty <= Options.CapPoints);
        }
    }

    [Fact]
    public void BountyForWeeks_极端周数不溢出且恒不超过上限()
    {
        for (var weeks = 1; weeks <= 1000; weeks++)
            Assert.InRange(ArenaBountyCalculator.BountyForWeeks(weeks, Options), 0, Options.CapPoints);
    }

    [Fact]
    public void Compute_运营手工SurvivalDays不参与计算()
    {
        // 纯函数只接 CreatedAtUtc + weekKey，无 SurvivalDays 入参，结构性保证手工字段无法影响结果。
        var created = WeekEndUtc.AddDays(-28);
        var a = ArenaBountyCalculator.Compute(created, WeekKey, Options);
        var b = ArenaBountyCalculator.Compute(created, WeekKey, new ArenaBountyOptions { CapPoints = 400 });
        Assert.Equal(a.Bounty, b.Bounty);
        Assert.Equal(400, a.Bounty);
    }
}
