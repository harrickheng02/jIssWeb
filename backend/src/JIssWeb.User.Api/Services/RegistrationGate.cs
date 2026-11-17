using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using JIssWeb.User.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace JIssWeb.User.Api.Services;

/// <summary>日上限检查结果。<see cref="Blocked"/> 为 true 时 <see cref="RetryAfterSeconds"/> 为距下一个本地零点的秒数。</summary>
public sealed record RegistrationGateDecision(bool Blocked, int RetryAfterSeconds)
{
    public static RegistrationGateDecision Pass { get; } = new(false, 0);

    public static RegistrationGateDecision Block(int retryAfterSeconds)
        => new(true, Math.Max(retryAfterSeconds, 1));
}

/// <summary>
/// 注册准入护栏：总闸状态、两级日上限的检查与计数。
///
/// 计数口径是「成功建号数」而非「请求数」——若在入口计数，几次参数校验失败就会吃掉配额，
/// 小范围开放时日上限可能只有几十，运营者会看到「上限用尽」却一个号都没建成。
/// 因此 <see cref="CheckAsync"/> 只读不写，<see cref="RecordSuccessAsync"/> 才递增。
///
/// 预检与计数分离带来一个有界竞态：并发请求可同时通过预检，实际注册数可能略微超过
/// DailyCap（超出量不超过并发数）。严格不超限需 Lua 脚本或分布式锁，为一个默认关闭的
/// 小范围开关引入这类复杂度不值得，此处明确接受。
/// </summary>
public sealed class RegistrationGate
{
    private const string KeyPrefix = "register:day";

    private readonly IDatabase _redis;
    private readonly RegistrationSettings _settings;
    private readonly ILogger<RegistrationGate> _logger;
    private readonly TimeZoneInfo _timeZone;

    public RegistrationGate(
        IConnectionMultiplexer redis,
        IOptions<RegistrationSettings> options,
        ILogger<RegistrationGate> logger)
    {
        _redis = redis.GetDatabase();
        _settings = options.Value;
        _logger = logger;
        _timeZone = ResolveTimeZone(_settings.TimeZoneId);
    }

    public bool Enabled => _settings.Enabled;

    /// <summary>只读预检。不消耗配额。</summary>
    public async Task<RegistrationGateDecision> CheckAsync(string? ip)
    {
        if (!_settings.Enabled)
            return RegistrationGateDecision.Pass;

        var retryAfter = SecondsUntilNextLocalMidnight();
        var today = LocalDateKey();

        if (_settings.DailyCap > 0)
        {
            var used = await ReadAsync(GlobalKey(today));
            if (used >= _settings.DailyCap)
                return RegistrationGateDecision.Block(retryAfter);
        }

        if (_settings.PerIpDailyCap > 0 && !string.IsNullOrWhiteSpace(ip))
        {
            var used = await ReadAsync(IpKey(today, ip));
            if (used >= _settings.PerIpDailyCap)
                return RegistrationGateDecision.Block(retryAfter);
        }

        return RegistrationGateDecision.Pass;
    }

    /// <summary>建号成功后调用。递增全局与 IP 两个计数。</summary>
    public async Task RecordSuccessAsync(string? ip)
    {
        if (!_settings.Enabled)
            return;

        var today = LocalDateKey();
        var ttl = TimeSpan.FromSeconds(SecondsUntilNextLocalMidnight());

        await IncrementAsync(GlobalKey(today), ttl);

        if (!string.IsNullOrWhiteSpace(ip))
            await IncrementAsync(IpKey(today, ip), ttl);
    }

    /// <summary>
    /// 今日剩余名额。总闸关闭返回 0；<see cref="RegistrationSettings.DailyCap"/> 为 0（不限制）返回 -1。
    /// </summary>
    public async Task<int> RemainingTodayAsync()
    {
        if (!_settings.Enabled)
            return 0;
        if (_settings.DailyCap <= 0)
            return -1;

        var used = await ReadAsync(GlobalKey(LocalDateKey()));
        return Math.Max(_settings.DailyCap - used, 0);
    }

    private string LocalDateKey()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private int SecondsUntilNextLocalMidnight()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        try
        {
            var local = TimeZoneInfo.ConvertTime(nowUtc, _timeZone);
            var nextMidnightLocal = local.Date.AddDays(1);
            var nextMidnight = new DateTimeOffset(nextMidnightLocal, _timeZone.GetUtcOffset(nextMidnightLocal));
            var seconds = (int)Math.Ceiling((nextMidnight - nowUtc).TotalSeconds);
            return Math.Max(seconds, 1);
        }
        catch (Exception ex)
        {
            // 夏令时切换的无效时刻等边界情况：不因一个时间计算让注册端点失败。
            _logger.LogWarning(ex, "Failed to compute seconds until local midnight for tz={TimeZoneId}", _settings.TimeZoneId);
            return 3600;
        }
    }

    private TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Registration:TimeZoneId={TimeZoneId} is not resolvable on this host, falling back to UTC", id);
            return TimeZoneInfo.Utc;
        }
    }

    private string GlobalKey(string date) => $"{KeyPrefix}:global:{date}";

    private string IpKey(string date, string ip) => $"{KeyPrefix}:ip:{Sha256(ip)}:{date}";

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task<int> ReadAsync(string key)
    {
        var value = await _redis.StringGetAsync(key);
        return value.HasValue && int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private async Task IncrementAsync(string key, TimeSpan ttl)
    {
        var count = await _redis.StringIncrementAsync(key);
        if (count == 1)
            await _redis.KeyExpireAsync(key, ttl);
    }
}
