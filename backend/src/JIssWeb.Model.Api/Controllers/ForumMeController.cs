using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/me")]
[Authorize]
public class ForumMeController : ControllerBase
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly ForumAuthorDisplayResolver _authorNames;
    private readonly ForumEngagementService _engagement;

    public ForumMeController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumAuthorDisplayResolver authorNames,
        ForumEngagementService engagement)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _authorNames = authorNames;
        _engagement = engagement;
    }

    [HttpGet("posts")]
    public async Task<ActionResult<ApiResult<PagedPostsDto>>> MyPosts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedPostsDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<PagedPostsDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        var filter = Builders<ForumPostRecord>.Filter.Eq(x => x.AuthorSubId, sub);
        var total = await _posts.CountDocumentsAsync(filter);
        var items = await _posts.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        var names = await _authorNames.ResolveAsync(items.Select(x => x.AuthorSubId));
        var dtos = items.Select(p => ForumDtoMapping.ToListItem(p, names)).ToList();
        if (dtos.Count > 0)
        {
            var ids = dtos.Select(x => x.Id).ToList();
            var (liked, fav) = await _engagement.GetEngagementSetsAsync(ids, sub);
            foreach (var d in dtos)
            {
                d.LikedByMe = liked.Contains(d.Id);
                d.FavoritedByMe = fav.Contains(d.Id);
            }
        }

        return Ok(ApiResult<PagedPostsDto>.Ok(new PagedPostsDto
        {
            Items = dtos,
            TotalCount = (int)total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpGet("replies")]
    public async Task<ActionResult<ApiResult<PagedRepliesDto>>> MyReplies(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedRepliesDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<PagedRepliesDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        var filter = Builders<ForumReplyRecord>.Filter.Eq(x => x.AuthorSubId, sub);
        var total = await _replies.CountDocumentsAsync(filter);
        var items = await _replies.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        var names = await _authorNames.ResolveAsync(items.Select(x => x.AuthorSubId));
        var dtos = items.Select(r => ForumDtoMapping.ToReplyDto(r, names)).ToList();
        return Ok(ApiResult<PagedRepliesDto>.Ok(new PagedRepliesDto
        {
            Items = dtos,
            TotalCount = (int)total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpGet("favorites")]
    public async Task<ActionResult<ApiResult<PagedPostsDto>>> MyFavorites(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedPostsDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        string sub;
        try
        {
            sub = User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<PagedPostsDto>.Fail("未授权", "UNAUTHORIZED"));
        }

        var (items, totalCount) = await _engagement.ListFavoritePostsAsync(sub, page, pageSize);
        var names = await _authorNames.ResolveAsync(items.Select(x => x.AuthorSubId));
        var dtos = items.Select(p => ForumDtoMapping.ToListItem(p, names)).ToList();
        if (dtos.Count > 0)
        {
            var ids = dtos.Select(x => x.Id).ToList();
            var (liked, fav) = await _engagement.GetEngagementSetsAsync(ids, sub);
            foreach (var d in dtos)
            {
                d.LikedByMe = liked.Contains(d.Id);
                d.FavoritedByMe = fav.Contains(d.Id);
            }
        }

        return Ok(ApiResult<PagedPostsDto>.Ok(new PagedPostsDto
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        }));
    }
}

public class PagedRepliesDto
{
    public List<ReplyDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
