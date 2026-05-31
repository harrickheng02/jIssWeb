using System.Linq;
using System.Security.Claims;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
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

    public bool CanModeratePostAsModerator(ClaimsPrincipal? user, string moderatorSub, ForumPostRecord post)
    {
        if (string.IsNullOrWhiteSpace(moderatorSub) || post is null) return false;

        var boardIds = ResolveModeratorBoardIds(user, moderatorSub);
        if (boardIds is null) return false;

        return PostBoardInModeratorScope(boardIds, post);
    }

    /// <summary>
    /// When the post document is gone, scope checks can use the persisted board label (e.g. from audit metadata) with the same rules as <see cref="CanModeratePostAsModerator"/>.
    /// </summary>
    public bool CanModerateBoardTitleAsModerator(ClaimsPrincipal? user, string moderatorSub, string boardTitle)
    {
        if (string.IsNullOrWhiteSpace(moderatorSub)) return false;

        var boardIds = ResolveModeratorBoardIds(user, moderatorSub);
        if (boardIds is null) return false;

        var title = (boardTitle ?? "").Trim();
        return PostBoardInModeratorScope(boardIds, new ForumPostRecord { Board = title });
    }

    /// <summary>
    /// Prefer when audit metadata carries stable <paramref name="boardId"/> (matches configured board id entries).
    /// </summary>
    public bool CanModerateBoardIdAsModerator(ClaimsPrincipal? user, string moderatorSub, string boardId)
    {
        if (string.IsNullOrWhiteSpace(moderatorSub) || string.IsNullOrWhiteSpace(boardId)) return false;

        var boardIds = ResolveModeratorBoardIds(user, moderatorSub);
        if (boardIds is null || boardIds.Count == 0) return false;

        var id = boardId.Trim();
        return boardIds.Any(b => string.Equals((b ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns a non-null, non-empty board id list when the moderator has a usable scope; otherwise null (caller should respond with forbidden).
    /// </summary>
    public IReadOnlyList<string>? GetModeratorBoardIdScope(ClaimsPrincipal? user, string moderatorSub)
    {
        var boardIds = ResolveModeratorBoardIds(user, moderatorSub);
        if (boardIds is null || boardIds.Count == 0) return null;
        return boardIds;
    }

    /// <summary>
    /// Board ids: non-empty <c>forumBoardIds</c> JWT claim wins; empty array in JWT falls back to <c>Forum:Moderation:Moderators</c> on model-service; missing claim uses server roster only (legacy tokens).
    /// </summary>
    private IReadOnlyList<string>? ResolveModeratorBoardIds(ClaimsPrincipal? user, string moderatorSub)
    {
        List<string>? jwtIds = null;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var role = user.GetForumPrincipalRole();
            if (role == ForumPrincipalRole.Moderator)
            {
                var raw = user.FindFirstValue(ForumBoardIdsClaim.Name);
                if (raw != null && ForumBoardIdsClaimJson.TryDeserialize(raw, out var parsed))
                {
                    jwtIds = parsed;
                }
            }
        }

        var entry = _moderation.Value.Moderators
            .FirstOrDefault(x => string.Equals((x.Sub ?? "").Trim(), moderatorSub.Trim(), StringComparison.Ordinal));

        var merged = new List<string>();
        if (jwtIds is { Count: > 0 })
            merged.AddRange(jwtIds);
        if (entry?.BoardIds is { Count: > 0 })
            merged.AddRange(entry.BoardIds);
        merged = merged
            .Select(id => (id ?? "").Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (merged.Count > 0)
            return merged;

        // 版主身份已确认但未配置板块范围时，默认可治理全部已配置板块
        if (user?.Identity?.IsAuthenticated == true && user.GetForumPrincipalRole() == ForumPrincipalRole.Moderator)
        {
            var allBoardIds = AllConfiguredBoardIds();
            if (allBoardIds.Count > 0)
                return allBoardIds;
        }

        return null;
    }

    private List<string> AllConfiguredBoardIds() =>
        _boards.Value.Boards
            .Select(b => (b.Id ?? "").Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private bool PostBoardInModeratorScope(IReadOnlyList<string>? boardIds, ForumPostRecord post)
    {
        if (boardIds is null || boardIds.Count == 0) return false;
        var postBoard = (post.Board ?? "").Trim();
        if (postBoard.Length == 0) return false;

        var postBoardId = ForumBoardIdLookup.ResolveBoardIdFromTitle(_boards.Value, postBoard);
        var allowedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in boardIds)
        {
            var token = (raw ?? "").Trim();
            if (token.Length == 0) continue;
            allowedIds.Add(token);
            var title = ForumBoardIdLookup.ResolveConfiguredBoardTitle(_boards.Value, token);
            if (!string.IsNullOrWhiteSpace(title))
                allowedTitles.Add(title.Trim());
            else
                allowedTitles.Add(token);
        }

        if (allowedTitles.Contains(postBoard)) return true;
        if (postBoardId is not null && allowedIds.Contains(postBoardId)) return true;
        return false;
    }
}
