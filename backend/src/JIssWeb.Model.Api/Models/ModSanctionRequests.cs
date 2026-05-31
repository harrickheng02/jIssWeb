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
}

public class ModUserSanctionResultDto
{
    public string SanctionId { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DurationPreset { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
