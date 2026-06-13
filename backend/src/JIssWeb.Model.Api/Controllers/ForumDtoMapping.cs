using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;

namespace JIssWeb.Model.Api.Controllers;

internal static class ForumDtoMapping
{
    internal static PostListItemDto ToListItem(ForumPostRecord p, IReadOnlyDictionary<string, string> names) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Excerpt = p.Excerpt,
        AuthorId = p.AuthorSubId,
        AuthorDisplayName = Display(p.AuthorSubId, names),
        PublishedAtUtc = p.CreatedAtUtc,
        Board = p.Board,
        Tags = p.Tags,
        IsSticky = p.IsSticky,
        RepliesLocked = p.RepliesLocked,
        IsFeatured = p.IsFeatured,
        Likes = p.LikeCount,
        FavoriteCount = p.FavoriteCount,
        Comments = p.CommentCount,
        Views = p.ViewCount,
        UpdatedAtUtc = p.UpdatedAtUtc,
        State = p.State,
        DeletedAtUtc = p.DeletedAtUtc,
        DeletedBySub = p.DeletedBySub,
    };

    internal static PostDetailDto MapDetail(ForumPostRecord p, IReadOnlyDictionary<string, string> names) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Body = p.Body,
        Excerpt = p.Excerpt,
        AuthorId = p.AuthorSubId,
        AuthorDisplayName = Display(p.AuthorSubId, names),
        PublishedAtUtc = p.CreatedAtUtc,
        Board = p.Board,
        Tags = p.Tags,
        IsSticky = p.IsSticky,
        RepliesLocked = p.RepliesLocked,
        IsFeatured = p.IsFeatured,
        Likes = p.LikeCount,
        FavoriteCount = p.FavoriteCount,
        Comments = p.CommentCount,
        Views = p.ViewCount,
        UpdatedAtUtc = p.UpdatedAtUtc,
        State = p.State,
        DeletedAtUtc = p.DeletedAtUtc,
        DeletedBySub = p.DeletedBySub,
    };

    internal static ReplyDto ToReplyDto(ForumReplyRecord r, IReadOnlyDictionary<string, string> names) => new()
    {
        Id = r.Id,
        PostId = r.PostId,
        AuthorId = r.AuthorSubId,
        AuthorDisplayName = Display(r.AuthorSubId, names),
        Body = r.Body,
        State = r.State,
        CreatedAtUtc = r.CreatedAtUtc,
        UpdatedAtUtc = r.UpdatedAtUtc,
    };

    internal static string DisplayNameFor(IReadOnlyDictionary<string, string> names, string sub) =>
        Display(sub, names);

    private static string Display(string? sub, IReadOnlyDictionary<string, string> names)
    {
        var key = sub ?? "";
        return names.TryGetValue(key, out var n) ? n : ForumDisplayName.ForSub(key);
    }
}
