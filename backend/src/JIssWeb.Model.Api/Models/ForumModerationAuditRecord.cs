namespace JIssWeb.Model.Api.Models;

public class ForumModerationAuditRecord
{
    public string Id { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Action { get; set; } = "";
    public string OperatorSub { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

