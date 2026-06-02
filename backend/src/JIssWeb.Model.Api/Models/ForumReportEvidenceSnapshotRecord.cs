namespace JIssWeb.Model.Api.Models;

public class ForumReportEvidenceReportSnapshot
{
    public string Id { get; set; } = "";
    public string ReporterSub { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string PostId { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string BoardTitle { get; set; } = "";
    public string? Reason { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? HandledBySub { get; set; }
    public DateTime? HandledAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBySub { get; set; }
}

public class ForumReportEvidenceTargetSnapshot
{
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public bool Tombstone { get; set; }
    public string? TombstoneReason { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? AuthorSubId { get; set; }
    public string? State { get; set; }
}

public class ForumReportEvidenceSnapshotRecord
{
    public string Id { get; set; } = "";
    public string ReportId { get; set; } = "";
    public string BoardId { get; set; } = "";
    public DateTime HandledAtUtc { get; set; }
    public DateTime SnapshottedAtUtc { get; set; }
    public ForumReportEvidenceReportSnapshot Report { get; set; } = new();
    public ForumReportEvidenceTargetSnapshot Target { get; set; } = new();
}
