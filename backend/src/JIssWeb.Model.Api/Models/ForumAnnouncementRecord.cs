namespace JIssWeb.Model.Api.Models;

public class ForumAnnouncementRecord
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? LinkUrl { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public bool Pinned { get; set; }
}
