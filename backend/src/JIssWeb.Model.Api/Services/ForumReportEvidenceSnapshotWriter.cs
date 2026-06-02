using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

internal static class ForumReportEvidenceSnapshotWriter
{
    internal static async Task TryWriteOnCloseAsync(
        IMongoCollection<ForumReportEvidenceSnapshotRecord> snapshots,
        IMongoCollection<ForumPostRecord> posts,
        IMongoCollection<ForumReplyRecord> replies,
        ILogger logger,
        ForumReportRecord report,
        CancellationToken ct)
    {
        if (!report.HandledAtUtc.HasValue)
            return;

        var canonical = CanonicalStatus(report.Status);
        if (canonical != ForumReportStatuses.Resolved && canonical != ForumReportStatuses.Rejected)
            return;

        var handledAt = report.HandledAtUtc.Value;
        var fb = Builders<ForumReportEvidenceSnapshotRecord>.Filter;
        var exists = await snapshots.Find(fb.And(
                fb.Eq(x => x.ReportId, report.Id),
                fb.Eq(x => x.HandledAtUtc, handledAt)))
            .Limit(1)
            .AnyAsync(ct);
        if (exists)
            return;

        try
        {
            var target = await CaptureTargetAsync(report, posts, replies, ct);
            var now = DateTime.UtcNow;
            await snapshots.InsertOneAsync(new ForumReportEvidenceSnapshotRecord
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                ReportId = report.Id,
                BoardId = report.BoardId,
                HandledAtUtc = handledAt,
                SnapshottedAtUtc = now,
                Report = ToReportSnapshot(report),
                Target = target,
            }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write evidence snapshot for report {ReportId}", report.Id);
        }
    }

    internal static ForumReportEvidenceReportSnapshot ToReportSnapshot(ForumReportRecord r) =>
        new()
        {
            Id = r.Id,
            ReporterSub = r.ReporterSub,
            TargetType = r.TargetType,
            TargetId = r.TargetId,
            PostId = r.PostId,
            BoardId = r.BoardId,
            BoardTitle = r.BoardTitle,
            Reason = r.Reason,
            Status = CanonicalStatus(r.Status),
            CreatedAtUtc = r.CreatedAtUtc,
            UpdatedAtUtc = r.UpdatedAtUtc,
            HandledBySub = r.HandledBySub,
            HandledAtUtc = r.HandledAtUtc,
            AcknowledgedAtUtc = r.AcknowledgedAtUtc,
            AcknowledgedBySub = r.AcknowledgedBySub,
        };

    internal static async Task<ForumReportEvidenceTargetSnapshot> CaptureTargetAsync(
        ForumReportRecord report,
        IMongoCollection<ForumPostRecord> posts,
        IMongoCollection<ForumReplyRecord> replies,
        CancellationToken ct)
    {
        if (string.Equals(report.TargetType, "post", StringComparison.OrdinalIgnoreCase))
        {
            var post = await posts.Find(x => x.Id == report.TargetId).FirstOrDefaultAsync(ct);
            if (post is null)
            {
                return new ForumReportEvidenceTargetSnapshot
                {
                    TargetType = "post",
                    TargetId = report.TargetId,
                    Tombstone = true,
                    TombstoneReason = "target_not_found",
                };
            }

            if (string.Equals(post.State, "deleted", StringComparison.Ordinal))
            {
                return new ForumReportEvidenceTargetSnapshot
                {
                    TargetType = "post",
                    TargetId = post.Id,
                    Tombstone = true,
                    TombstoneReason = "target_deleted",
                    Title = post.Title,
                    Body = post.Body,
                    AuthorSubId = post.AuthorSubId,
                    State = post.State,
                };
            }

            return new ForumReportEvidenceTargetSnapshot
            {
                TargetType = "post",
                TargetId = post.Id,
                Title = post.Title,
                Body = post.Body,
                AuthorSubId = post.AuthorSubId,
                State = post.State,
            };
        }

        if (string.Equals(report.TargetType, "reply", StringComparison.OrdinalIgnoreCase))
        {
            var reply = await replies.Find(x => x.Id == report.TargetId).FirstOrDefaultAsync(ct);
            if (reply is null)
            {
                return new ForumReportEvidenceTargetSnapshot
                {
                    TargetType = "reply",
                    TargetId = report.TargetId,
                    Tombstone = true,
                    TombstoneReason = "target_not_found",
                };
            }

            if (string.Equals(reply.State, "deleted", StringComparison.Ordinal))
            {
                return new ForumReportEvidenceTargetSnapshot
                {
                    TargetType = "reply",
                    TargetId = reply.Id,
                    Tombstone = true,
                    TombstoneReason = "target_deleted",
                    Body = reply.Body,
                    AuthorSubId = reply.AuthorSubId,
                    State = reply.State,
                };
            }

            return new ForumReportEvidenceTargetSnapshot
            {
                TargetType = "reply",
                TargetId = reply.Id,
                Body = reply.Body,
                AuthorSubId = reply.AuthorSubId,
                State = reply.State,
            };
        }

        return new ForumReportEvidenceTargetSnapshot
        {
            TargetType = report.TargetType,
            TargetId = report.TargetId,
            Tombstone = true,
            TombstoneReason = "unknown_target_type",
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
