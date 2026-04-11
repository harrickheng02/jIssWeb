namespace JIssWeb.Model.Api.Models;

public class ForumReplyRecord
{
    public string Id { get; set; } = "";
    public string PostId { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
