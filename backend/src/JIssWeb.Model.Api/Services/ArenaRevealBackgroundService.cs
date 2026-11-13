using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场每周揭露调度（Model.Api 第 4 个 HostedService，D2）。
/// - 周五 RevealHourLocal（本地 Asia/Shanghai，缺 tzdata 降级 +8）触发，揭露上一自然周；
/// - ArenaReveal:Enabled=false 时整体不调度（便于灰度与测试）；
/// - ArenaReveal:CatchUpEnabled=true 时启动补跑最近 MaxCatchUpWeeks 个错过的 weekKey；
/// - 每周经 ArenaRevealSchedule 计算触发点与揭露周键，调用 IArenaRevealService（weekKey 原子领取保证多实例不重复，D13）。
/// </summary>
public sealed class ArenaRevealBackgroundService : BackgroundService
{
    private const int MaxCatchUpWeeks = 12;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ArenaRevealOptions> _options;
    private readonly ILogger<ArenaRevealBackgroundService> _logger;

    public ArenaRevealBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ArenaRevealOptions> options,
        ILogger<ArenaRevealBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation(
                "ArenaRevealBackgroundService 未启用（ArenaReveal:Enabled=false），跳过调度。");
            return;
        }

        if (opts.CatchUpEnabled)
        {
            try
            {
                await CatchUpAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arena reveal catch-up failed.");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextRun = ArenaRevealSchedule.ComputeNextRunUtc(
                    DateTimeOffset.UtcNow, opts.RevealHourLocal, opts.TimeZoneId);
                var delay = nextRun - DateTimeOffset.UtcNow;
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested) break;

                var weekKey = ArenaRevealSchedule.ComputeRevealWeekKey(DateTimeOffset.UtcNow, opts.TimeZoneId);
                await RunRevealAsync(weekKey, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arena reveal scheduled pass failed.");
            }
        }
    }

    private async Task CatchUpAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        for (var i = 1; i <= MaxCatchUpWeeks; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var weekKey = ArenaRevealSchedule.WeekKeyForLocalDate(
                DateTimeOffset.UtcNow, -7 * i, opts.TimeZoneId);
            await RunRevealAsync(weekKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunRevealAsync(string weekKey, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArenaRevealService>();
        var outcome = await service.RevealWeekAsync(weekKey, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Arena reveal week {WeekKey} outcome={Outcome}.", weekKey, outcome);
    }
}
