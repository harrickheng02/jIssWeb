using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

internal static class ForumReportModerationAuditWriter
{
    internal static async Task TryWriteAcknowledgeAsync(
        IMongoCollection<ForumModerationAuditRecord> audit,
        ILogger logger,
        ForumReportRecord report,
        string operatorSub,
        CancellationToken ct)
    {
        var fb = Builders<ForumModerationAuditRecord>.Filter;
        var exists = await audit.Find(fb.And(
                fb.Eq(x => x.TargetType, "report"),
                fb.Eq(x => x.TargetId, report.Id),
                fb.Eq(x => x.Action, "report.acknowledge")))
            .Limit(1)
            .AnyAsync(ct);
        if (exists)
            return;

        await InsertAsync(audit, logger, report, "report.acknowledge", operatorSub, null, ct);
    }

    internal static async Task TryWriteStatusCloseAsync(
        IMongoCollection<ForumModerationAuditRecord> audit,
        ILogger logger,
        ForumReportRecord report,
        string operatorSub,
        string storedStatus,
        DateTime handledAtUtc,
        CancellationToken ct)
    {
        if (storedStatus != ForumReportStatuses.Resolved && storedStatus != ForumReportStatuses.Rejected)
            return;

        var action = storedStatus == ForumReportStatuses.Resolved ? "report.resolve" : "report.reject";
        var fb = Builders<ForumModerationAuditRecord>.Filter;
        var exists = await audit.Find(fb.And(
                fb.Eq(x => x.TargetType, "report"),
                fb.Eq(x => x.TargetId, report.Id),
                fb.Eq(x => x.Action, action),
                fb.Eq("Metadata.handledAtUtc", handledAtUtc)))
            .Limit(1)
            .AnyAsync(ct);
        if (exists)
            return;

        await InsertAsync(audit, logger, report, action, operatorSub, handledAtUtc, ct);
    }

    private static async Task InsertAsync(
        IMongoCollection<ForumModerationAuditRecord> audit,
        ILogger logger,
        ForumReportRecord report,
        string action,
        string operatorSub,
        DateTime? handledAtUtc,
        CancellationToken ct)
    {
        var meta = new Dictionary<string, object>
        {
            ["reportId"] = report.Id,
            ["postId"] = report.PostId,
            ["boardId"] = report.BoardId,
        };
        if (handledAtUtc.HasValue)
            meta["handledAtUtc"] = handledAtUtc.Value;

        try
        {
            await audit.InsertOneAsync(new ForumModerationAuditRecord
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                TargetType = "report",
                TargetId = report.Id,
                Action = action,
                OperatorSub = operatorSub,
                OccurredAtUtc = DateTime.UtcNow,
                Metadata = meta,
            }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write report moderation audit {Action} for report {ReportId}", action, report.Id);
        }
    }
}
