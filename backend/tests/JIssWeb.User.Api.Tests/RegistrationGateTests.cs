using JIssWeb.User.Api.Options;
using JIssWeb.User.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.User.Api.Tests;

/// <summary>
/// <see cref="RegistrationGate"/> 的单元测试。用字典语义的假 Redis，
/// 因此断言的是真实的计数累积行为，而不是「mock 永远返回 1」这种空转。
/// </summary>
public sealed class RegistrationGateTests
{
    // ------------------------------------------------------------------
    // 假 Redis：INCR 累加、GET 读取，EXPIRE 记录调用以便断言 TTL
    // ------------------------------------------------------------------

    private sealed class FakeRedis
    {
        private readonly Dictionary<string, long> _counters = new();

        public List<TimeSpan?> Expiries { get; } = [];

        public IConnectionMultiplexer Multiplexer { get; }

        public FakeRedis()
        {
            var db = new Mock<IDatabase>();

            db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync((RedisKey key, long value, CommandFlags _) =>
              {
                  var k = key.ToString();
                  _counters[k] = _counters.GetValueOrDefault(k) + value;
                  return _counters[k];
              });

            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync((RedisKey key, CommandFlags _) =>
                  _counters.TryGetValue(key.ToString(), out var n) ? (RedisValue)n : RedisValue.Null);

            // 三种重载都打桩，避免因为签名绑定差异漏记 TTL
            db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync((RedisKey _, TimeSpan? expiry, CommandFlags _) =>
              {
                  Expiries.Add(expiry);
                  return true;
              });
            db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync((RedisKey _, TimeSpan? expiry, ExpireWhen _, CommandFlags _) =>
              {
                  Expiries.Add(expiry);
                  return true;
              });
            db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<DateTime?>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);

            var mux = new Mock<IConnectionMultiplexer>();
            mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
            Multiplexer = mux.Object;
        }
    }

    private static RegistrationGate CreateGate(
        FakeRedis redis,
        bool enabled = true,
        int dailyCap = 0,
        int perIpDailyCap = 0,
        string timeZoneId = "Asia/Shanghai")
        => new(
            redis.Multiplexer,
            // 全限定：JIssWeb.User.Api.Options 命名空间会遮蔽 Microsoft.Extensions.Options.Options
            Microsoft.Extensions.Options.Options.Create(new RegistrationSettings
            {
                Enabled = enabled,
                DailyCap = dailyCap,
                PerIpDailyCap = perIpDailyCap,
                TimeZoneId = timeZoneId
            }),
            NullLogger<RegistrationGate>.Instance);

    // ------------------------------------------------------------------
    // 总闸
    // ------------------------------------------------------------------

    [Fact]
    public void Disabled_gate_reports_not_enabled()
    {
        var gate = CreateGate(new FakeRedis(), enabled: false);
        Assert.False(gate.Enabled);
    }

    [Fact]
    public async Task Disabled_gate_never_blocks_and_reports_zero_remaining()
    {
        var redis = new FakeRedis();
        var gate = CreateGate(redis, enabled: false, dailyCap: 1, perIpDailyCap: 1);

        // 先塞满配额（关闭态下 RecordSuccess 应为 no-op，因此计数不会增长）
        await gate.RecordSuccessAsync("1.1.1.1");
        await gate.RecordSuccessAsync("1.1.1.1");

        var decision = await gate.CheckAsync("1.1.1.1");
        Assert.False(decision.Blocked);
        Assert.Equal(0, await gate.RemainingTodayAsync());
        Assert.Empty(redis.Expiries);
    }

    // ------------------------------------------------------------------
    // 全局日上限
    // ------------------------------------------------------------------

    [Fact]
    public async Task Global_cap_blocks_once_reached()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 2);

        await gate.RecordSuccessAsync(null);
        await gate.RecordSuccessAsync(null);

        var decision = await gate.CheckAsync(null);
        Assert.True(decision.Blocked);
        Assert.True(decision.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task Global_cap_allows_registration_below_limit()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 3);

        await gate.RecordSuccessAsync(null);

        var decision = await gate.CheckAsync(null);
        Assert.False(decision.Blocked);
    }

    [Fact]
    public async Task Zero_daily_cap_means_unlimited()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 0);

        for (var i = 0; i < 50; i++)
            await gate.RecordSuccessAsync(null);

        Assert.False((await gate.CheckAsync(null)).Blocked);
        Assert.Equal(-1, await gate.RemainingTodayAsync());
    }

    // ------------------------------------------------------------------
    // 单 IP 日上限
    // ------------------------------------------------------------------

    [Fact]
    public async Task Per_ip_cap_blocks_only_the_offending_ip()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 0, perIpDailyCap: 1);

        await gate.RecordSuccessAsync("1.1.1.1");

        Assert.True((await gate.CheckAsync("1.1.1.1")).Blocked);
        Assert.False((await gate.CheckAsync("2.2.2.2")).Blocked);
    }

    [Fact]
    public async Task Missing_ip_skips_per_ip_check()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 0, perIpDailyCap: 1);

        await gate.RecordSuccessAsync("1.1.1.1");

        // 拿不到 IP 时不应因为「空 IP 的计数」而误拒
        Assert.False((await gate.CheckAsync(null)).Blocked);
        Assert.False((await gate.CheckAsync("  ")).Blocked);
    }

    // ------------------------------------------------------------------
    // 计数口径
    // ------------------------------------------------------------------

    [Fact]
    public async Task Check_alone_does_not_consume_quota()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 2);

        for (var i = 0; i < 5; i++)
            Assert.False((await gate.CheckAsync(null)).Blocked);

        Assert.Equal(2, await gate.RemainingTodayAsync());
    }

    [Fact]
    public async Task Record_success_consumes_both_counters()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 5, perIpDailyCap: 1);

        await gate.RecordSuccessAsync("1.1.1.1");

        Assert.Equal(4, await gate.RemainingTodayAsync());
        Assert.True((await gate.CheckAsync("1.1.1.1")).Blocked);
        Assert.False((await gate.CheckAsync("2.2.2.2")).Blocked);
    }

    [Fact]
    public async Task Remaining_today_reflects_usage()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 5);

        await gate.RecordSuccessAsync(null);
        await gate.RecordSuccessAsync(null);

        Assert.Equal(3, await gate.RemainingTodayAsync());
    }

    [Fact]
    public async Task Remaining_today_never_goes_negative()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 1);

        await gate.RecordSuccessAsync(null);
        await gate.RecordSuccessAsync(null); // 竞态下允许略微超出

        Assert.Equal(0, await gate.RemainingTodayAsync());
    }

    // ------------------------------------------------------------------
    // 日界与 TTL
    // ------------------------------------------------------------------

    [Fact]
    public async Task Counter_ttl_is_within_one_day()
    {
        var redis = new FakeRedis();
        var gate = CreateGate(redis, dailyCap: 10);

        await gate.RecordSuccessAsync(null);

        var ttl = Assert.Single(redis.Expiries);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 1, 86400);
    }

    [Fact]
    public async Task Unresolvable_time_zone_does_not_break_the_gate()
    {
        var gate = CreateGate(new FakeRedis(), dailyCap: 1, timeZoneId: "Not/A_Real_Zone");

        Assert.False((await gate.CheckAsync(null)).Blocked);
        await gate.RecordSuccessAsync(null);
        Assert.True((await gate.CheckAsync(null)).Blocked);
    }
}
