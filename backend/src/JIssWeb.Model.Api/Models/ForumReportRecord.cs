namespace JIssWeb.Model.Api.Models;

public static class ForumReportStatuses
{
    public const string Pending = "pending";
    /// <summary>Historical stored value before governance-full; normalized to <see cref="Rejected"/> in API projections.</summary>
    public const string Dismissed = "dismissed";
    /// <summary>Historical stored value before governance-full; normalized to <see cref="Resolved"/> in API projections.</summary>
    public const string Acknowledged = "acknowledged";
    public const string Rejected = "rejected";
    public const string Resolved = "resolved";
}

public class ForumReportRecord
{
    public string Id { get; set; } = "";
    public string ReporterSub { get; set; } = "";
    /// <summary>post or reply</summary>
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    /// <summary>Thread id for deep links (post id).</summary>
    public string PostId { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string BoardTitle { get; set; } = "";
    public string? Reason { get; set; }
    /// <summary>Legacy governance field; no longer written.</summary>
    public string? ResolutionCode { get; set; }
    public string Status { get; set; } = ForumReportStatuses.Pending;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? HandledBySub { get; set; }
    public DateTime? HandledAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBySub { get; set; }
}
