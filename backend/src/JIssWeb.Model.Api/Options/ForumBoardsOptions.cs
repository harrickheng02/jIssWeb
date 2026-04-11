namespace JIssWeb.Model.Api.Options;

public class ForumBoardsOptions
{
    public const string SectionName = "Forum";

    public List<ForumBoardEntry> Boards { get; set; } = new();
}

public class ForumBoardEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}
