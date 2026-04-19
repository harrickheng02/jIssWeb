namespace JIssWeb.Model.Api.Models;

public class ForumPostFavoriteRecord
{
    public string Id { get; set; } = "";
    public string PostId { get; set; } = "";
    public string UserSubId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
