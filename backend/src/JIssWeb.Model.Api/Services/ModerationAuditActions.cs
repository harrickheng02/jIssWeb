namespace JIssWeb.Model.Api.Services;

internal static class ModerationAuditActions
{
    internal static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "post.setSticky",
        "post.unsetSticky",
        "post.lockReplies",
        "post.unlockReplies",
        "post.setFeatured",
        "post.unsetFeatured",
        "post.modDelete",
        "reply.modDelete",
        "user.warn",
        "user.mute",
        "user.unmute",
        "report.updateStatus",
        "report.resolve",
        "report.reject",
        "report.acknowledge",
        "report.statusChange",
        "audit.export",
    };

    internal static bool TryParseQueryActions(IEnumerable<string>? rawValues, out List<string> actions, out string? invalid)
    {
        actions = new List<string>();
        invalid = null;
        if (rawValues is null)
            return true;

        foreach (var raw in rawValues)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Known.Contains(part))
                {
                    if (!actions.Contains(part, StringComparer.Ordinal))
                        actions.Add(part);
                }
                else
                {
                    invalid = part;
                    return false;
                }
            }
        }

        return true;
    }
}
