using System.Linq;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumModerationAccessService
{
    private readonly IOptions<ForumModerationOptions> _moderation;
    private readonly IOptions<ForumBoardsOptions> _boards;

    public ForumModerationAccessService(
        IOptions<ForumModerationOptions> moderation,
        IOptions<ForumBoardsOptions> boards)
    {
        _moderation = moderation;
        _boards = boards;
    }

    public bool CanModeratePostAsModerator(string moderatorSub, ForumPostRecord post)
    {
        if (string.IsNullOrWhiteSpace(moderatorSub) || post is null) return false;

        var entry = _moderation.Value.Moderators
            .FirstOrDefault(x => string.Equals((x.Sub ?? "").Trim(), moderatorSub.Trim(), StringComparison.Ordinal));
        if (entry is null) return false;

        return PostBoardInModeratorScope(entry.BoardIds, post);
    }

    /// <summary>
    /// When the post document is gone, scope checks can use the persisted board label (e.g. from audit metadata) with the same rules as <see cref="CanModeratePostAsModerator"/>.
    /// </summary>
    public bool CanModerateBoardTitleAsModerator(string moderatorSub, string boardTitle)
    {
        if (string.IsNullOrWhiteSpace(moderatorSub)) return false;

        var entry = _moderation.Value.Moderators
            .FirstOrDefault(x => string.Equals((x.Sub ?? "").Trim(), moderatorSub.Trim(), StringComparison.Ordinal));
        if (entry is null) return false;

        var title = (boardTitle ?? "").Trim();
        return PostBoardInModeratorScope(entry.BoardIds, new ForumPostRecord { Board = title });
    }

    /// <summary>
    /// Prefer when audit metadata carries stable <paramref name="boardId"/> (matches <c>Moderators[].boardIds</c> entries).
    /// </summary>
    public bool CanModerateBoardIdAsModerator(string moderatorSub, string boardId)
    {
        if (string.IsNullOrWhiteSpace(moderatorSub) || string.IsNullOrWhiteSpace(boardId)) return false;

        var entry = _moderation.Value.Moderators
            .FirstOrDefault(x => string.Equals((x.Sub ?? "").Trim(), moderatorSub.Trim(), StringComparison.Ordinal));
        if (entry is null || entry.BoardIds is null || entry.BoardIds.Count == 0) return false;

        var id = boardId.Trim();
        return entry.BoardIds.Any(b => string.Equals((b ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase));
    }

    private bool PostBoardInModeratorScope(IReadOnlyList<string>? boardIds, ForumPostRecord post)
    {
        if (boardIds is null || boardIds.Count == 0) return false;
        var boardTitles = boardIds
            .Select(id => ForumBoardIdLookup.ResolveConfiguredBoardTitle(_boards.Value, id))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return boardTitles.Contains((post.Board ?? "").Trim());
    }
}

