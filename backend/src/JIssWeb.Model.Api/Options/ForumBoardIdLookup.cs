namespace JIssWeb.Model.Api.Options;

public static class ForumBoardIdLookup
{
    public static string? ResolveConfiguredBoardTitle(ForumBoardsOptions options, string? boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId)) return null;
        var id = boardId.Trim();
        return options.Boards?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))?.Title?.Trim();
    }

    /// <summary>Inverse of <see cref="ResolveConfiguredBoardTitle"/> for persisting stable ids in audit metadata.</summary>
    public static string? ResolveBoardIdFromTitle(ForumBoardsOptions options, string? boardTitle)
    {
        if (string.IsNullOrWhiteSpace(boardTitle)) return null;
        var t = boardTitle.Trim();
        return options.Boards?
            .FirstOrDefault(x => string.Equals(x.Title?.Trim(), t, StringComparison.OrdinalIgnoreCase))
            ?.Id?.Trim();
    }
}
