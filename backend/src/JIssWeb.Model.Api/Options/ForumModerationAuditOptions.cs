namespace JIssWeb.Model.Api.Options;

public sealed class ForumModerationAuditOptions
{
    public const string SectionName = "Forum:ModerationAudit";

    /// <summary>When feed/export omit fromUtc and toUtc, include rows from this many days before request time (UTC).</summary>
    public int DefaultFeedDays { get; set; } = 30;

    /// <summary>Maximum rows returned by CSV export; exceeding count yields EXPORT_TOO_LARGE.</summary>
    public int MaxExportRows { get; set; } = 5000;
}
