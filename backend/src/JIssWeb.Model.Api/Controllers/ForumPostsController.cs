using System.Collections.Generic;
using System.Text.RegularExpressions;
using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/posts")]
public class ForumPostsController : ControllerBase
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IMongoCollection<InAppNotificationRecord> _notifications;
    private readonly IOptions<ForumBoardsOptions> _boardOptions;
    private readonly ForumAuthorDisplayResolver _authorNames;
    private readonly ForumEngagementService _engagement;
    private readonly ILogger<ForumPostsController> _logger;

    public ForumPostsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boardOptions,
        ForumAuthorDisplayResolver authorNames,
        ForumEngagementService engagement,
        ILogger<ForumPostsController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _notifications = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        _boardOptions = boardOptions;
        _authorNames = authorNames;
        _engagement = engagement;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<PagedPostsDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? boardId = null)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedPostsDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        FilterDefinition<ForumPostRecord>? boardFilter = null;
        if (!string.IsNullOrWhiteSpace(boardId))
        {
            var boardTitle = TryResolveBoardTitle(boardId);
            if (boardTitle is null)
                return BadRequest(ApiResult<PagedPostsDto>.Fail("无效的板块", "INVALID_BOARD_ID"));
            boardFilter = Builders<ForumPostRecord>.Filter.Eq(x => x.Board, boardTitle);
        }

        FilterDefinition<ForumPostRecord>? searchFilter = null;
        if (Request.Query.TryGetValue("q", out var qVals))
        {
            var trimmed = qVals.ToString().Trim();
            if (trimmed.Length == 0)
                return BadRequest(ApiResult<PagedPostsDto>.Fail("搜索关键词无效", "INVALID_SEARCH_QUERY"));
            searchFilter = BuildKeywordFilter(trimmed);
        }

        FilterDefinition<ForumPostRecord>? tagFilter = null;
        if (Request.Query.TryGetValue("tag", out var tagVals))
        {
            var tagTrimmed = tagVals.ToString().Trim();
            if (tagTrimmed.Length == 0)
                return BadRequest(ApiResult<PagedPostsDto>.Fail("标签参数无效", "INVALID_TAG_QUERY"));
            tagFilter = BuildTagFilter(tagTrimmed);
        }

        var parts = new List<FilterDefinition<ForumPostRecord>>();
        if (boardFilter is not null) parts.Add(boardFilter);
        if (searchFilter is not null) parts.Add(searchFilter);
        if (tagFilter is not null) parts.Add(tagFilter);
        var filter = parts.Count == 0
            ? FilterDefinition<ForumPostRecord>.Empty
            : Builders<ForumPostRecord>.Filter.And(parts);

        string? sortMode = null;
        if (Request.Query.TryGetValue("sort", out var sortVals))
        {
            var st = sortVals.ToString().Trim();
            if (st.Length > 0)
            {
                if (!string.Equals(st, "latest", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(st, "hot", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(ApiResult<PagedPostsDto>.Fail("排序参数无效", "INVALID_SORT"));
                sortMode = string.Equals(st, "hot", StringComparison.OrdinalIgnoreCase) ? "hot" : "latest";
            }
        }

        var useHotSort = searchFilter is null && sortMode == "hot";
        var keywordSearchActive = searchFilter is not null;

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

        var total = await _posts.CountDocumentsAsync(filter);
        var find = _posts.Find(filter);
        var sortDef = BuildPostListSortDefinition(useHotSort, keywordSearchActive);
        var items = await find
            .Sort(sortDef)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        var names = await _authorNames.ResolveAsync(items.Select(x => x.AuthorSubId));
        var dtos = items.Select(p => ForumDtoMapping.ToListItem(p, names)).ToList();
        var viewer = TryGetAuthorId();
        if (viewer is not null && dtos.Count > 0)
            await ApplyEngagementToListItemsAsync(dtos, viewer);

        return Ok(ApiResult<PagedPostsDto>.Ok(new PagedPostsDto
        {
            Items = dtos,
            TotalCount = (int)total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpGet("{postId}/replies")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<List<ReplyDto>>>> GetReplies(string postId)
    {
        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null)
            return NotFound(ApiResult<List<ReplyDto>>.Fail("未找到", "NOT_FOUND"));

        var list = await _replies.Find(x => x.PostId == postId)
            .SortBy(x => x.CreatedAtUtc)
            .ToListAsync();
        var replyNames = await _authorNames.ResolveAsync(list.Select(x => x.AuthorSubId));
        return Ok(ApiResult<List<ReplyDto>>.Ok(list.Select(r => ForumDtoMapping.ToReplyDto(r, replyNames)).ToList()));
    }

    [HttpPost("{postId}/replies")]
    [Authorize]
    public async Task<ActionResult<ApiResult<ReplyDto>>> CreateReply(string postId, [FromBody] CreateReplyRequest request)
    {
        var authorId = TryGetAuthorId();
        if (authorId is null)
            return Unauthorized(ApiResult<ReplyDto>.Fail("未授权", "UNAUTHORIZED"));

        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(ApiResult<ReplyDto>.Fail("内容不能为空", "INVALID_INPUT"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null)
            return NotFound(ApiResult<ReplyDto>.Fail("未找到", "NOT_FOUND"));

        if (post.RepliesLocked)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<ReplyDto>.Fail("本帖已禁止回复", "REPLIES_LOCKED"));

        var now = DateTime.UtcNow;
        var reply = new ForumReplyRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            PostId = postId,
            AuthorSubId = authorId,
            Body = request.Body.Trim(),
            CreatedAtUtc = now,
        };
        await _replies.InsertOneAsync(reply);
        await _posts.UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update.Inc(x => x.CommentCount, 1));

        if (!string.Equals(post.AuthorSubId, authorId, StringComparison.Ordinal))
        {
            var notif = new InAppNotificationRecord
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                RecipientSubId = post.AuthorSubId,
                Type = InAppNotificationTypes.ReplyToPost,
                PostId = postId,
                ReplyId = reply.Id,
                ActorSubId = authorId,
                PostTitle = post.Title,
                ReadAtUtc = null,
                CreatedAtUtc = now,
            };
            try
            {
                await _notifications.InsertOneAsync(notif);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
            {
                _logger.LogDebug(ex, "Skipped duplicate notification for reply {ReplyId}", reply.Id);
            }
        }

        var replyNames = await _authorNames.ResolveAsync(new[] { reply.AuthorSubId });
        return Ok(ApiResult<ReplyDto>.Ok(ForumDtoMapping.ToReplyDto(reply, replyNames)));
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<PostDetailDto>>> GetById(string id)
    {
        var post = await _posts.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (post is null)
            return NotFound(ApiResult<PostDetailDto>.Fail("未找到", "NOT_FOUND"));

        await _posts.UpdateOneAsync(x => x.Id == id, Builders<ForumPostRecord>.Update.Inc(x => x.ViewCount, 1));
        post.ViewCount += 1;

        var detailNames = await _authorNames.ResolveAsync(new[] { post.AuthorSubId });
        var dto = ForumDtoMapping.MapDetail(post, detailNames);
        var viewer = TryGetAuthorId();
        if (viewer is not null)
        {
            var snap = await _engagement.GetSnapshotAsync(id, viewer);
            if (snap is not null)
            {
                dto.LikedByMe = snap.LikedByMe;
                dto.FavoritedByMe = snap.FavoritedByMe;
            }
        }

        return Ok(ApiResult<PostDetailDto>.Ok(dto));
    }

    [HttpPost("{postId}/like")]
    [Authorize]
    public async Task<ActionResult<ApiResult<PostEngagementStateDto>>> LikePost(string postId)
    {
        var sub = TryGetAuthorId();
        if (sub is null)
            return Unauthorized(ApiResult<PostEngagementStateDto>.Fail("未授权", "UNAUTHORIZED"));

        var snap = await _engagement.LikeAsync(postId, sub);
        if (snap is null)
            return NotFound(ApiResult<PostEngagementStateDto>.Fail("未找到", "NOT_FOUND"));

        return Ok(ApiResult<PostEngagementStateDto>.Ok(ToEngagementState(postId, snap)));
    }

    [HttpDelete("{postId}/like")]
    [Authorize]
    public async Task<ActionResult<ApiResult<PostEngagementStateDto>>> UnlikePost(string postId)
    {
        var sub = TryGetAuthorId();
        if (sub is null)
            return Unauthorized(ApiResult<PostEngagementStateDto>.Fail("未授权", "UNAUTHORIZED"));

        var snap = await _engagement.UnlikeAsync(postId, sub);
        if (snap is null)
            return NotFound(ApiResult<PostEngagementStateDto>.Fail("未找到", "NOT_FOUND"));

        return Ok(ApiResult<PostEngagementStateDto>.Ok(ToEngagementState(postId, snap)));
    }

    [HttpPost("{postId}/favorite")]
    [Authorize]
    public async Task<ActionResult<ApiResult<PostEngagementStateDto>>> FavoritePost(string postId)
    {
        var sub = TryGetAuthorId();
        if (sub is null)
            return Unauthorized(ApiResult<PostEngagementStateDto>.Fail("未授权", "UNAUTHORIZED"));

        var snap = await _engagement.FavoriteAsync(postId, sub);
        if (snap is null)
            return NotFound(ApiResult<PostEngagementStateDto>.Fail("未找到", "NOT_FOUND"));

        return Ok(ApiResult<PostEngagementStateDto>.Ok(ToEngagementState(postId, snap)));
    }

    [HttpDelete("{postId}/favorite")]
    [Authorize]
    public async Task<ActionResult<ApiResult<PostEngagementStateDto>>> UnfavoritePost(string postId)
    {
        var sub = TryGetAuthorId();
        if (sub is null)
            return Unauthorized(ApiResult<PostEngagementStateDto>.Fail("未授权", "UNAUTHORIZED"));

        var snap = await _engagement.UnfavoriteAsync(postId, sub);
        if (snap is null)
            return NotFound(ApiResult<PostEngagementStateDto>.Fail("未找到", "NOT_FOUND"));

        return Ok(ApiResult<PostEngagementStateDto>.Ok(ToEngagementState(postId, snap)));
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ApiResult<CreatePostResultDto>>> Create([FromBody] CreatePostRequest request)
    {
        var authorId = TryGetAuthorId();
        if (authorId is null)
            return Unauthorized(ApiResult<CreatePostResultDto>.Fail("未授权", "UNAUTHORIZED"));

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(ApiResult<CreatePostResultDto>.Fail("标题不能为空", "INVALID_INPUT"));
        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(ApiResult<CreatePostResultDto>.Fail("正文不能为空", "INVALID_INPUT"));

        var boardResolved = TryResolveBoardForCreate(request);
        if (boardResolved.Error is not null)
            return BadRequest(ApiResult<CreatePostResultDto>.Fail(boardResolved.Error, boardResolved.Code));

        var tagsResult = NormalizeCreateTags(request.Tags);
        if (tagsResult.Error is not null)
            return BadRequest(ApiResult<CreatePostResultDto>.Fail(tagsResult.Error, tagsResult.Code));

        var body = request.Body.Trim();
        var now = DateTime.UtcNow;
        var doc = new ForumPostRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Title = request.Title.Trim(),
            Body = body,
            Excerpt = MakeExcerpt(body),
            AuthorSubId = authorId,
            Board = boardResolved.Title!,
            Tags = tagsResult.Tags!,
            CreatedAtUtc = now,
        };
        await _posts.InsertOneAsync(doc);
        return Ok(ApiResult<CreatePostResultDto>.Ok(new CreatePostResultDto { Id = doc.Id }));
    }

    private static FilterDefinition<ForumPostRecord> BuildKeywordFilter(string keyword)
    {
        var escaped = Regex.Escape(keyword);
        var rx = new BsonRegularExpression(escaped, "i");
        var (titleField, authorField) = ForumMongoSetup.GetPostSearchBsonFields();
        return Builders<ForumPostRecord>.Filter.Or(
            Builders<ForumPostRecord>.Filter.Regex(titleField, rx),
            Builders<ForumPostRecord>.Filter.Regex(authorField, rx));
    }

    /// <summary>
    /// Non-search lists: sticky first, then latest/hot rules. Keyword search (<c>q</c>): recency only; <see cref="ForumPostRecord.IsSticky"/> is returned for display only.
    /// </summary>
    private static SortDefinition<ForumPostRecord> BuildPostListSortDefinition(bool useHotSort, bool keywordSearchActive)
    {
        if (keywordSearchActive)
        {
            return Builders<ForumPostRecord>.Sort
                .Descending(x => x.CreatedAtUtc)
                .Ascending(x => x.Id);
        }

        return useHotSort
            ? Builders<ForumPostRecord>.Sort
                .Descending(x => x.IsSticky)
                .Descending(x => x.LikeCount)
                .Descending(x => x.CommentCount)
                .Descending(x => x.ViewCount)
                .Descending(x => x.CreatedAtUtc)
                .Ascending(x => x.Id)
            : Builders<ForumPostRecord>.Sort
                .Descending(x => x.IsSticky)
                .Descending(x => x.CreatedAtUtc);
    }

    private const int MaxTagCount = 10;
    private const int MaxTagLength = 32;

    private static (List<string>? Tags, string? Error, string? Code) NormalizeCreateTags(List<string>? tags)
    {
        if (tags is null || tags.Count == 0)
            return (new List<string>(), null, null);

        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var t = raw.Trim();
            if (t.Length > MaxTagLength)
                return (null, "单个标签过长", "INVALID_TAGS");
            if (!seen.Add(t)) continue;
            if (list.Count >= MaxTagCount)
                return (null, "标签数量过多", "INVALID_TAGS");
            list.Add(t);
        }

        return (list, null, null);
    }

    private static FilterDefinition<ForumPostRecord> BuildTagFilter(string trimmedTag)
    {
        var escaped = Regex.Escape(trimmedTag);
        var rx = new BsonRegularExpression($"^{escaped}$", "i");
        return Builders<ForumPostRecord>.Filter.Regex(nameof(ForumPostRecord.Tags), rx);
    }

    private static string MakeExcerpt(string body, int maxLen = 200)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var t = body.Trim().Replace('\r', ' ').Replace('\n', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        if (t.Length <= maxLen) return t;
        return t[..maxLen] + "…";
    }

    private string? TryGetAuthorId()
    {
        try
        {
            return User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? TryResolveBoardTitle(string? boardId) =>
        ForumBoardIdLookup.ResolveConfiguredBoardTitle(_boardOptions.Value, boardId);

    private bool IsKnownBoardTitle(string title)
    {
        var t = title.Trim();
        return _boardOptions.Value.Boards.Any(x => string.Equals(x.Title, t, StringComparison.OrdinalIgnoreCase));
    }

    private string DefaultBoardTitle()
    {
        var g = _boardOptions.Value.Boards.FirstOrDefault(x => x.Id.Equals("general", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(g?.Title)) return g.Title.Trim();
        var first = _boardOptions.Value.Boards.FirstOrDefault();
        return string.IsNullOrWhiteSpace(first?.Title) ? "综合" : first.Title.Trim();
    }

    private (string? Title, string? Error, string? Code) TryResolveBoardForCreate(CreatePostRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.BoardId))
        {
            var t = TryResolveBoardTitle(request.BoardId);
            if (t is null) return (null, "无效的板块", "INVALID_BOARD_ID");
            return (t, null, null);
        }

        if (!string.IsNullOrWhiteSpace(request.Board))
        {
            var raw = request.Board.Trim();
            if (!IsKnownBoardTitle(raw)) return (null, "无效的板块", "INVALID_BOARD");
            var match = _boardOptions.Value.Boards.First(x => string.Equals(x.Title, raw, StringComparison.OrdinalIgnoreCase));
            return (match.Title.Trim(), null, null);
        }

        return (DefaultBoardTitle(), null, null);
    }

    private static PostEngagementStateDto ToEngagementState(string postId, PostEngagementSnapshot snap) => new()
    {
        PostId = postId,
        LikeCount = snap.LikeCount,
        FavoriteCount = snap.FavoriteCount,
        LikedByMe = snap.LikedByMe,
        FavoritedByMe = snap.FavoritedByMe,
    };

    private async Task ApplyEngagementToListItemsAsync(IReadOnlyList<PostListItemDto> items, string userSubId)
    {
        if (items.Count == 0) return;
        var ids = items.Select(x => x.Id).ToList();
        var (liked, favorited) = await _engagement.GetEngagementSetsAsync(ids, userSubId);
        foreach (var d in items)
        {
            d.LikedByMe = liked.Contains(d.Id);
            d.FavoritedByMe = favorited.Contains(d.Id);
        }
    }
}

public class PagedPostsDto
{
    public List<PostListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class PostListItemDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public DateTime PublishedAtUtc { get; set; }
    public string Board { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public bool IsSticky { get; set; }
    public bool RepliesLocked { get; set; }
    public int Likes { get; set; }
    public int FavoriteCount { get; set; }
    public int Comments { get; set; }
    public int Views { get; set; }
    public bool LikedByMe { get; set; }
    public bool FavoritedByMe { get; set; }
}

public class PostEngagementStateDto
{
    public string PostId { get; set; } = "";
    public int LikeCount { get; set; }
    public int FavoriteCount { get; set; }
    public bool LikedByMe { get; set; }
    public bool FavoritedByMe { get; set; }
}

public class PostDetailDto : PostListItemDto
{
    public string Body { get; set; } = "";
}

public class CreatePostRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? BoardId { get; set; }
    public string? Board { get; set; }
    public List<string>? Tags { get; set; }
}

public class CreatePostResultDto
{
    public string Id { get; set; } = "";
}

public class CreateReplyRequest
{
    public string? Body { get; set; }
}

public class ReplyDto
{
    public string Id { get; set; } = "";
    public string PostId { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
