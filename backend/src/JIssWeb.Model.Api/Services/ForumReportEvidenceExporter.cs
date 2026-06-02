using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

internal static class ForumReportEvidenceExporter
{
    private static readonly string[] SanctionActions = ["user.warn", "user.mute", "user.unmute"];

    internal static async Task<bool> HasHistoricalEvidenceTraceAsync(
        IMongoCollection<ForumModerationAuditRecord> audit,
        string reportId,
        CancellationToken ct)
    {
        var fb = Builders<ForumModerationAuditRecord>.Filter;
        return await audit.Find(fb.Or(
                fb.And(fb.Eq(x => x.TargetType, "report"), fb.Eq(x => x.TargetId, reportId)),
                fb.Eq("Metadata.reportId", reportId)))
            .Limit(1)
            .AnyAsync(ct);
    }

    internal static async Task<byte[]?> TryBuildZipAsync(
        IMongoCollection<ForumReportEvidenceSnapshotRecord> snapshots,
        IMongoCollection<ForumModerationAuditRecord> audit,
        IMongoCollection<ForumPostRecord> posts,
        IMongoCollection<ForumReplyRecord> replies,
        ForumReportRecord? report,
        ForumReportEvidenceSnapshotRecord? snapshot,
        string exportedBySub,
        CancellationToken ct)
    {
        ForumReportEvidenceReportSnapshot reportPayload;
        ForumReportEvidenceTargetSnapshot targetPayload;
        string reportId;
        string postId;

        if (report is not null)
        {
            var canonical = CanonicalStatus(report.Status);
            if (canonical == ForumReportStatuses.Pending)
                return null;

            reportId = report.Id;
            postId = report.PostId;
            var snap = snapshot
                       ?? await FindSnapshotAsync(snapshots, report.Id, report.HandledAtUtc, ct);
            if (snap is not null)
            {
                reportPayload = snap.Report;
                targetPayload = snap.Target;
            }
            else
            {
                reportPayload = ForumReportEvidenceSnapshotWriter.ToReportSnapshot(report);
                targetPayload = await ForumReportEvidenceSnapshotWriter.CaptureTargetAsync(report, posts, replies, ct);
            }
        }
        else if (snapshot is not null)
        {
            reportId = snapshot.ReportId;
            postId = snapshot.Report.PostId;
            reportPayload = snapshot.Report;
            targetPayload = snapshot.Target;
        }
        else
        {
            return null;
        }

        var auditRows = await LoadThreadAuditAsync(audit, reportId, postId, ct);
        var sanctions = BuildSanctionSummaries(auditRows, reportId);
        var manifest = new EvidenceManifest
        {
            ReportId = reportId,
            ExportedAtUtc = DateTime.UtcNow,
            ExportedBySub = exportedBySub,
        };

        return EvidenceZipBuilder.Build(new EvidenceBundleInput
        {
            Manifest = manifest,
            Report = reportPayload,
            Target = targetPayload,
            ThreadAudit = auditRows.Select(ToAuditExportDto).ToList(),
            SanctionsSummary = sanctions,
        });
    }

    internal static async Task<ForumReportEvidenceSnapshotRecord?> FindSnapshotAsync(
        IMongoCollection<ForumReportEvidenceSnapshotRecord> snapshots,
        string reportId,
        DateTime? handledAtUtc,
        CancellationToken ct)
    {
        var fb = Builders<ForumReportEvidenceSnapshotRecord>.Filter;
        if (handledAtUtc.HasValue)
        {
            return await snapshots.Find(fb.And(
                    fb.Eq(x => x.ReportId, reportId),
                    fb.Eq(x => x.HandledAtUtc, handledAtUtc.Value)))
                .FirstOrDefaultAsync(ct);
        }

        return await snapshots.Find(fb.Eq(x => x.ReportId, reportId))
            .SortByDescending(x => x.HandledAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<List<ForumModerationAuditRecord>> LoadThreadAuditAsync(
        IMongoCollection<ForumModerationAuditRecord> audit,
        string reportId,
        string postId,
        CancellationToken ct)
    {
        var fb = Builders<ForumModerationAuditRecord>.Filter;
        var onReport = fb.And(
            fb.Eq(x => x.TargetType, "report"),
            fb.Eq(x => x.TargetId, reportId));
        var metaReport = fb.Eq("Metadata.reportId", reportId);
        FilterDefinition<ForumModerationAuditRecord> filter;
        if (!string.IsNullOrWhiteSpace(postId))
        {
            var thread = PostThreadAuditQuery.BuildThreadFilter(postId);
            filter = fb.Or(onReport, metaReport, thread);
        }
        else
        {
            filter = fb.Or(onReport, metaReport);
        }

        return await audit.Find(filter)
            .SortByDescending(x => x.OccurredAtUtc)
            .ToListAsync(ct);
    }

    internal static List<EvidenceSanctionSummary> BuildSanctionSummaries(
        IEnumerable<ForumModerationAuditRecord> rows,
        string reportId)
    {
        var list = new List<EvidenceSanctionSummary>();
        foreach (var row in rows)
        {
            if (!SanctionActions.Contains(row.Action, StringComparer.Ordinal))
                continue;
            if (!MetadataReportIdMatches(row.Metadata, reportId))
                continue;

            list.Add(new EvidenceSanctionSummary
            {
                Action = row.Action,
                OperatorSub = row.OperatorSub,
                OccurredAtUtc = row.OccurredAtUtc,
                Reason = GetMetaString(row.Metadata, "reason"),
                DurationPreset = GetMetaString(row.Metadata, "durationPreset"),
                TargetSub = string.Equals(row.TargetType, "user", StringComparison.Ordinal) ? row.TargetId : null,
            });
        }

        return list;
    }

    private static object ToAuditExportDto(ForumModerationAuditRecord row) => new
    {
        row.Id,
        row.TargetType,
        row.TargetId,
        row.Action,
        row.OperatorSub,
        row.OccurredAtUtc,
        row.Metadata,
    };

    private static bool MetadataReportIdMatches(Dictionary<string, object>? meta, string reportId) =>
        string.Equals(GetMetaString(meta, "reportId"), reportId, StringComparison.Ordinal);

    private static string? GetMetaString(Dictionary<string, object>? meta, string key)
    {
        if (meta is null || !meta.TryGetValue(key, out var raw) || raw is null)
            return null;
        return raw switch
        {
            string s => s,
            _ => raw.ToString(),
        };
    }

    private static string CanonicalStatus(string? stored)
    {
        var s = (stored ?? "").Trim().ToLowerInvariant();
        if (s == ForumReportStatuses.Dismissed) return ForumReportStatuses.Rejected;
        if (s == ForumReportStatuses.Acknowledged) return ForumReportStatuses.Resolved;
        return s;
    }
}
