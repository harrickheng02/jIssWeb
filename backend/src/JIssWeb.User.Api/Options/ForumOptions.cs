namespace JIssWeb.User.Api.Options;

/// <summary>Forum-related configuration (e.g. local role overrides for QA).</summary>
public class ForumOptions
{
    public const string SectionName = "Forum";

    /// <summary>Maps user id (<c>sub</c>) to forum role string: <c>member</c>, <c>moderator</c>, or <c>admin</c>.</summary>
    public Dictionary<string, string> RoleOverrides { get; set; } = new(StringComparer.Ordinal);
}
