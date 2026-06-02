using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

internal static class PostThreadAuditQuery
{
    internal static FilterDefinition<ForumModerationAuditRecord> BuildThreadFilter(string postId)
    {
        var fb = Builders<ForumModerationAuditRecord>.Filter;
        var onPost = fb.And(fb.Eq(x => x.TargetType, "post"), fb.Eq(x => x.TargetId, postId));
        var replyOnThread = fb.And(
            fb.Eq(x => x.TargetType, "reply"),
            fb.Eq(x => x.Action, "reply.modDelete"),
            fb.Eq("Metadata.postId", postId));
        var reportOnThread = fb.And(
            fb.Eq(x => x.TargetType, "report"),
            fb.Eq("Metadata.postId", postId));
        var userOnThread = fb.And(
            fb.Eq(x => x.TargetType, "user"),
            fb.In(x => x.Action, new[] { "user.warn", "user.mute", "user.unmute" }),
            fb.Eq("Metadata.postId", postId));
        return fb.Or(onPost, replyOnThread, reportOnThread, userOnThread);
    }

    internal static FilterDefinition<ForumModerationAuditRecord> ApplyOptionalFilters(
        FilterDefinition<ForumModerationAuditRecord> baseFilter,
        IReadOnlyList<string>? actions,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var fb = Builders<ForumModerationAuditRecord>.Filter;
        var filter = baseFilter;
        if (actions is { Count: > 0 })
            filter &= fb.In(x => x.Action, actions);
        if (fromUtc.HasValue)
            filter &= fb.Gte(x => x.OccurredAtUtc, fromUtc.Value);
        if (toUtc.HasValue)
            filter &= fb.Lte(x => x.OccurredAtUtc, toUtc.Value);
        return filter;
    }
}
