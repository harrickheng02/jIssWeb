namespace JIssWeb.Common.Security;

/// <summary>JWT claim name and allowed string values for global forum role (see openspec token-identity-consistency).</summary>
public static class ForumRoleClaim
{
    public const string Name = "forumRole";

    public const string Member = "member";
    public const string Moderator = "moderator";
    public const string Admin = "admin";
}
