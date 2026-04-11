namespace JIssWeb.Model.Api.Models;

public class ForumPostRecord
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public string Board { get; set; } = "综合";
    public List<string> Tags { get; set; } = new();
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public int ViewCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
