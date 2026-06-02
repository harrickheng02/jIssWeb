using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// Hard-deletes closed forum report rows past retention. Pending reports are never matched.
/// Aligns with governance audit: <c>forum_moderation_audit</c> is not modified here.
/// </summary>
public static class ForumReportRetentionPurger
{
    private static readonly string[] ClosedStatuses =
    [
        ForumReportStatuses.Rejected,
        ForumReportStatuses.Resolved,
        ForumReportStatuses.Dismissed,
        ForumReportStatuses.Acknowledged,
    ];

    /// <summary>
    /// Deletes documents with a closed status, non-null <see cref="ForumReportRecord.HandledAtUtc"/> strictly before the cutoff.
    /// </summary>
    /// <returns>MongoDB deleted count.</returns>
    public static async Task<long> PurgeStaleClosedAsync(
        IMongoCollection<ForumReportRecord> reports,
        int closedRetentionDays,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (closedRetentionDays < 1)
            closedRetentionDays = 1;

        var cutoff = utcNow.AddDays(-closedRetentionDays);
        FilterDefinitionBuilder<ForumReportRecord> fb = Builders<ForumReportRecord>.Filter;
        var filter = fb.And(
            fb.In(x => x.Status, ClosedStatuses),
            fb.Ne(x => x.HandledAtUtc, null),
            fb.Lt(x => x.HandledAtUtc, cutoff));

        var res = await reports.DeleteManyAsync(filter, cancellationToken);
        return res.DeletedCount;
    }

    /// <summary>
    /// Deletes evidence snapshots whose <see cref="ForumReportEvidenceSnapshotRecord.HandledAtUtc"/> is strictly before the cutoff.
    /// </summary>
    public static async Task<long> PurgeStaleEvidenceSnapshotsAsync(
        IMongoCollection<ForumReportEvidenceSnapshotRecord> snapshots,
        int closedRetentionDays,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (closedRetentionDays < 1)
            closedRetentionDays = 1;

        var cutoff = utcNow.AddDays(-closedRetentionDays);
        FilterDefinitionBuilder<ForumReportEvidenceSnapshotRecord> fb = Builders<ForumReportEvidenceSnapshotRecord>.Filter;
        var filter = fb.Lt(x => x.HandledAtUtc, cutoff);

        var res = await snapshots.DeleteManyAsync(filter, cancellationToken);
        return res.DeletedCount;
    }
}
