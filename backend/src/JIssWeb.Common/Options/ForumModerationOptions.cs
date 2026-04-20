namespace JIssWeb.Common.Options;

/// <summary>
/// Matches configuration section <c>Forum:Moderation</c>. Also nested under <c>Forum.Moderation</c> in user-service.
/// </summary>
public class ForumModerationOptions
{
    public const string SectionName = "Forum:Moderation";

    public List<ForumModeratorEntry> Moderators { get; set; } = new();
}

public class ForumModeratorEntry
{
    public string Sub { get; set; } = "";

    /// <summary>Board ids as in <c>Forum:Boards[].Id</c> (e.g. <c>general</c>).</summary>
    public List<string> BoardIds { get; set; } = new();
}
