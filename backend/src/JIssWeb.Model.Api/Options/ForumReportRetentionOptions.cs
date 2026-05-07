namespace JIssWeb.Model.Api.Options;

/// <summary>TTL-style cleanup for forum report documents in <c>forum_reports</c> only; moderation audit is unchanged.</summary>
public sealed class ForumReportRetentionOptions
{
    public const string SectionName = "Forum:ReportRetention";

    /// <summary>When false, the background purge loop is skipped.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Deletes closed reports whose handled timestamp is older than this many days.</summary>
    public int ClosedRetentionDays { get; set; } = 120;

    /// <summary>Minimum wait between purge passes.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>Delay before the first purge after process start.</summary>
    public int StartupDelayMinutes { get; set; } = 2;
}
