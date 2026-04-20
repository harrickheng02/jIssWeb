namespace JIssWeb.Model.Api.Options;

public class ForumModerationOptions
{
    public const string SectionName = "Forum:Moderation";

    public List<ForumModeratorEntry> Moderators { get; set; } = new();
}

public class ForumModeratorEntry
{
    public string Sub { get; set; } = "";
    public List<string> BoardIds { get; set; } = new();
}

