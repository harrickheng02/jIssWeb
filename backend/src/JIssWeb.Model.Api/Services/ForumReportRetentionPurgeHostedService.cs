using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>Runs <see cref="ForumReportRetentionPurger"/> on a timer while <see cref="ForumReportRetentionOptions.Enabled"/> is true.</summary>
public sealed class ForumReportRetentionPurgeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ForumReportRetentionPurgeHostedService> _logger;

    public ForumReportRetentionPurgeHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ForumReportRetentionPurgeHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupOpts = RefreshOptions();
        var startupDelay = TimeSpan.FromMinutes(Math.Clamp(startupOpts.StartupDelayMinutes, 0, 1440));
        if (startupDelay > TimeSpan.Zero)
            await Task.Delay(startupDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = RefreshOptions();
            try
            {
                if (opts.Enabled)
                    await PurgePassAsync(opts, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Forum report retention purge pass failed.");
            }

            opts = RefreshOptions();
            var interval = TimeSpan.FromHours(Math.Clamp(opts.IntervalHours, 1, 168));
            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }

    private ForumReportRetentionOptions RefreshOptions()
    {
        using var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptionsMonitor<ForumReportRetentionOptions>>().CurrentValue;
    }

    private async Task PurgePassAsync(ForumReportRetentionOptions opts, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var dbName = scope.ServiceProvider.GetRequiredService<IOptions<MongoSettings>>().Value.DatabaseName;

        var db = mongo.GetDatabase(dbName);
        var coll = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var snapshots = db.GetCollection<ForumReportEvidenceSnapshotRecord>(ForumMongoSetup.ReportEvidenceSnapshotsCollectionName);
        var utcNow = DateTime.UtcNow;
        var days = opts.ClosedRetentionDays;
        var deleted = await ForumReportRetentionPurger.PurgeStaleClosedAsync(coll, days, utcNow, ct).ConfigureAwait(false);
        var snapshotsDeleted = await ForumReportRetentionPurger.PurgeStaleEvidenceSnapshotsAsync(snapshots, days, utcNow, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Forum report retention purge removed {Deleted} closed report(s) and {SnapshotsDeleted} evidence snapshot(s) with HandledAtUtc older than {Days} days (UTC now {UtcNow:O}).",
            deleted,
            snapshotsDeleted,
            days < 1 ? 1 : days,
            utcNow);
    }
}
