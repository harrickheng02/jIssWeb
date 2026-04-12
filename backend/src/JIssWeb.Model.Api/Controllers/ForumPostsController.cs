using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/posts")]
public class ForumPostsController : ControllerBase
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IOptions<ForumBoardsOptions> _boardOptions;

    public ForumPostsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boardOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _boardOptions = boardOptions;
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

        FilterDefinition<ForumPostRecord> filter = FilterDefinition<ForumPostRecord>.Empty;
        if (!string.IsNullOrWhiteSpace(boardId))
        {
            var boardTitle = TryResolveBoardTitle(boardId);
            if (boardTitle is null)
                return BadRequest(ApiResult<PagedPostsDto>.Fail("无效的板块", "INVALID_BOARD_ID"));
            filter = Builders<ForumPostRecord>.Filter.Eq(x => x.Board, boardTitle);
        }

        var total = await _posts.CountDocumentsAsync(filter);
        var items = await _posts.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        var dtos = items.Select(ForumDtoMapping.ToListItem).ToList();
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
        return Ok(ApiResult<List<ReplyDto>>.Ok(list.Select(ForumDtoMapping.ToReplyDto).ToList()));
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

        return Ok(ApiResult<ReplyDto>.Ok(ForumDtoMapping.ToReplyDto(reply)));
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

        var dto = ForumDtoMapping.MapDetail(post);
        return Ok(ApiResult<PostDetailDto>.Ok(dto));
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
            Tags = request.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList() ?? new List<string>(),
            CreatedAtUtc = now,
        };
        await _posts.InsertOneAsync(doc);
        return Ok(ApiResult<CreatePostResultDto>.Ok(new CreatePostResultDto { Id = doc.Id }));
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

    private string? TryResolveBoardTitle(string? boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId)) return null;
        var id = boardId.Trim();
        return _boardOptions.Value.Boards.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))?.Title?.Trim();
    }

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
    public DateTime PublishedAtUtc { get; set; }
    public string Board { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public int Likes { get; set; }
    public int Comments { get; set; }
    public int Views { get; set; }
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
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
