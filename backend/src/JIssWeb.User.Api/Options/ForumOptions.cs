using JIssWeb.Common.Options;

namespace JIssWeb.User.Api.Options;

/// <summary>Forum-related configuration: role overrides (e.g. admin) and moderator board scope (single source for JWT <c>forumBoardIds</c>).</summary>
public class ForumOptions
{
    public const string SectionName = "Forum";

    /// <summary>Maps user id (<c>sub</c>) to forum role string: <c>member</c>, <c>moderator</c>, or <c>admin</c>. Prefer <see cref="Moderation"/> for moderators with board ids.</summary>
    public Dictionary<string, string> RoleOverrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Same shape as model-service <c>Forum:Moderation</c>. Issued into access token as <c>forumBoardIds</c> JSON array so model-service does not need a duplicate roster for authorized users.</summary>
    public ForumModerationOptions Moderation { get; set; } = new();
}
