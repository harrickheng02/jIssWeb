using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

internal static class AuditFeedQuery
{
    internal static (DateTime FromUtc, DateTime ToUtc) ResolveTimeWindow(
        DateTime? fromUtc,
        DateTime? toUtc,
        int defaultFeedDays)
    {
        var to = toUtc ?? DateTime.UtcNow;
        var from = fromUtc ?? to.AddDays(-Math.Max(1, defaultFeedDays));
        return (DateTime.SpecifyKind(from.ToUniversalTime(), DateTimeKind.Utc),
            DateTime.SpecifyKind(to.ToUniversalTime(), DateTimeKind.Utc));
    }

    internal static FilterDefinition<ForumModerationAuditRecord> BuildFeedFilter(
        ForumPrincipalRole role,
        IReadOnlyList<string>? moderatorBoardIds,
        string? boardId,
        IReadOnlyList<string>? actions,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var fb = Builders<ForumModerationAuditRecord>.Filter;
        var filter = fb.And(
            fb.Gte(x => x.OccurredAtUtc, fromUtc),
            fb.Lte(x => x.OccurredAtUtc, toUtc));

        if (actions is { Count: > 0 })
            filter &= fb.In(x => x.Action, actions);

        if (!string.IsNullOrWhiteSpace(boardId))
        {
            filter &= fb.Eq("Metadata.boardId", boardId.Trim());
        }
        else if (role == ForumPrincipalRole.Moderator && moderatorBoardIds is { Count: > 0 })
        {
            filter &= fb.In("Metadata.boardId", moderatorBoardIds);
        }

        if (actions is not { Count: > 0 })
            filter &= fb.Ne(x => x.Action, "audit.export");

        return filter;
    }
}

internal static class AuditFeedMetadata
{
    internal static string? GetString(Dictionary<string, object>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var raw) || raw is null)
            return null;
        var s = raw switch
        {
            string x => x.Trim(),
            _ => raw.ToString()?.Trim(),
        };
        return string.IsNullOrEmpty(s) ? null : s;
    }

    internal static string? ResolvePostId(ForumModerationAuditRecord record)
    {
        var fromMeta = GetString(record.Metadata, "postId");
        if (!string.IsNullOrEmpty(fromMeta))
            return fromMeta;
        if (string.Equals(record.TargetType, "post", StringComparison.OrdinalIgnoreCase))
            return record.TargetId;
        return null;
    }

    internal static string? ResolveReportId(ForumModerationAuditRecord record)
    {
        var fromMeta = GetString(record.Metadata, "reportId");
        if (!string.IsNullOrEmpty(fromMeta))
            return fromMeta;
        if (string.Equals(record.TargetType, "report", StringComparison.OrdinalIgnoreCase))
            return record.TargetId;
        return null;
    }

    internal static string ResolveBoardLabel(string? boardId, string? metadataBoardTitle, IReadOnlyDictionary<string, string> boardIdToTitle)
    {
        var fromMeta = metadataBoardTitle?.Trim();
        if (!string.IsNullOrEmpty(fromMeta))
            return fromMeta;
        if (!string.IsNullOrEmpty(boardId) && boardIdToTitle.TryGetValue(boardId, out var title) && !string.IsNullOrWhiteSpace(title))
            return title;
        if (!string.IsNullOrEmpty(boardId))
            return boardId;
        return "未知";
    }
}
