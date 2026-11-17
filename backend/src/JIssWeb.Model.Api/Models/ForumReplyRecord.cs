namespace JIssWeb.Model.Api.Models;

public class ForumReplyRecord
{
    public string Id { get; set; } = "";
    public string PostId { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string State { get; set; } = "published";  // published | deleted | pending_review
    /// <summary>AI 版主阶段一：命中的判定类别（spam / sexual / political / other）。</summary>
    public string? ModerationFlag { get; set; }
    /// <summary>AI 版主阶段一：判定置信度（0–1）。</summary>
    public double? ModerationConfidence { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBySub { get; set; }
}
