using JIssWeb.Model.Api.Models;

namespace JIssWeb.Model.Api.Controllers;

internal static class ForumDtoMapping
{
    internal static PostListItemDto ToListItem(ForumPostRecord p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Excerpt = p.Excerpt,
        AuthorId = p.AuthorSubId,
        PublishedAtUtc = p.CreatedAtUtc,
        Board = p.Board,
        Tags = p.Tags,
        Likes = p.LikeCount,
        Comments = p.CommentCount,
        Views = p.ViewCount,
    };

    internal static PostDetailDto MapDetail(ForumPostRecord p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Body = p.Body,
        Excerpt = p.Excerpt,
        AuthorId = p.AuthorSubId,
        PublishedAtUtc = p.CreatedAtUtc,
        Board = p.Board,
        Tags = p.Tags,
        Likes = p.LikeCount,
        Comments = p.CommentCount,
        Views = p.ViewCount,
    };

    internal static ReplyDto ToReplyDto(ForumReplyRecord r) => new()
    {
        Id = r.Id,
        PostId = r.PostId,
        AuthorId = r.AuthorSubId,
        Body = r.Body,
        CreatedAtUtc = r.CreatedAtUtc,
    };
}
