namespace JIssWeb.Common.Security;

/// <summary>
/// JWT claim carrying JSON array of board ids (e.g. <c>["general","tech"]</c>) the moderator may operate on.
/// Issued by user-service; model-service trusts it when the access token is valid.
/// </summary>
public static class ForumBoardIdsClaim
{
    public const string Name = "forumBoardIds";
}
