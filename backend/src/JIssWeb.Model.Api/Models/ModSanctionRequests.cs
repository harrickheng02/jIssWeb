namespace JIssWeb.Model.Api.Models;

public class ModReportContextRequest
{
    public string? ReportId { get; set; }
    public string? Reason { get; set; }
}

public class ModUserSanctionRequest
{
    public string Type { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ReportId { get; set; } = "";
    public string? DurationPreset { get; set; }
}

public class ModReportSanctionRequest
{
    public string Type { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? DurationPreset { get; set; }
}

public class ModRevokeSanctionRequest
{
    public string RevokeReason { get; set; } = "";
    /// <summary>可选；与举报上下文解封时传入，用于审计 metadata 关联帖线程。</summary>
    public string? ReportId { get; set; }
}

public class ModUserSanctionResultDto
{
    public string SanctionId { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DurationPreset { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
