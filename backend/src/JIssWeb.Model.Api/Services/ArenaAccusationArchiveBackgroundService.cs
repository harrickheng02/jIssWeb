using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场指认记录归档后台任务（G8/D3）：按日频清理已结算且超保留期的指认记录。
/// 独立于揭露服务，即便揭露服务未启用也照常运行；仅删除结算完成的记录（不早于结算）。
/// </summary>
public sealed class ArenaAccusationArchiveBackgroundService : BackgroundService
{
    private readonly IArenaAccusationArchiveService _archive;
    private readonly ArenaRevealOptions _options;
    private readonly ILogger<ArenaAccusationArchiveBackgroundService> _logger;

    // 归档按日频：启动后先跑一次，之后每 24h 一次。
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public ArenaAccusationArchiveBackgroundService(
        IArenaAccusationArchiveService archive,
        IOptions<ArenaRevealOptions> options,
        ILogger<ArenaAccusationArchiveBackgroundService> logger)
    {
        _archive = archive;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动即补跑一次，避免服务长期未启动导致堆积。
        await RunPurgeAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPurgeAsync(stoppingToken);
        }
    }

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.ArchiveRetentionDays));
            await _archive.PurgeAsync(cutoff, ct);
        }
        catch (Exception ex) when (ct.IsCancellationRequested is false)
        {
            _logger.LogError(ex, "Arena accusation archive purge failed; will retry next cycle.");
        }
    }
}
