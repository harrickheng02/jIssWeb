namespace JIssWeb.Model.Api.Options;

public static class ForumBoardIdLookup
{
    public static string? ResolveConfiguredBoardTitle(ForumBoardsOptions options, string? boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId)) return null;
        var id = boardId.Trim();
        return options.Boards?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))?.Title?.Trim();
    }
}
