using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/posts/drafts")]
[Authorize]
public class ForumDraftsController : ControllerBase
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumTagRecord> _tags;
    private readonly IOptions<ForumBoardsOptions> _boardOptions;
    private readonly ForumAuthorDisplayResolver _authorNames;

    public ForumDraftsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boardOptions,
        ForumAuthorDisplayResolver authorNames)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _tags = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
        _boardOptions = boardOptions;
        _authorNames = authorNames;
    }

    // POST api/forum/posts/drafts
    [HttpPost]
    public async Task<ActionResult<ApiResult<PublishDraftResultDto>>> CreateDraft([FromBody] CreateDraftRequest request)
    {
        var sub = GetSub();
        if (sub is null) return Unauthorized(ApiResult<PublishDraftResultDto>.Fail("未授权", "UNAUTHORIZED"));

        // Tags normalization (optional)
        var tagsResult = NormalizeTags(request.Tags);
        if (tagsResult.Error is not null)
            return BadRequest(ApiResult<PublishDraftResultDto>.Fail(tagsResult.Error, tagsResult.Code));

        var now = DateTime.UtcNow;
        var doc = new ForumPostRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Title = request.Title?.Trim() ?? "",
            Body = request.Body?.Trim() ?? "",
            Excerpt = MakeExcerpt(request.Body?.Trim() ?? ""),
            AuthorSubId = sub,
            Board = ResolveBoard(request.BoardId) ?? DefaultBoard(),
            Tags = tagsResult.Tags ?? new List<string>(),
            State = "draft",
            CreatedAtUtc = now,
        };
        await _posts.InsertOneAsync(doc);
        return Ok(ApiResult<PublishDraftResultDto>.Ok(new PublishDraftResultDto { Id = doc.Id, State = "draft" }));
    }

    // PUT api/forum/posts/drafts/{draftId}
    [HttpPut("{draftId}")]
    public async Task<ActionResult<ApiResult<PostDetailDto>>> UpdateDraft(string draftId, [FromBody] CreateDraftRequest request)
    {
        var sub = GetSub();
        if (sub is null) return Unauthorized(ApiResult<PostDetailDto>.Fail("未授权", "UNAUTHORIZED"));

        var draft = await _posts.Find(x => x.Id == draftId && x.State == "draft").FirstOrDefaultAsync();
        if (draft is null) return NotFound(ApiResult<PostDetailDto>.Fail("未找到", "NOT_FOUND"));
        if (!string.Equals(draft.AuthorSubId, sub, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<PostDetailDto>.Fail("无权操作", "FORBIDDEN"));

        var tagsResult = NormalizeTags(request.Tags);
        if (tagsResult.Error is not null)
            return BadRequest(ApiResult<PostDetailDto>.Fail(tagsResult.Error, tagsResult.Code));

        var update = Builders<ForumPostRecord>.Update.Set(x => x.UpdatedAtUtc, DateTime.UtcNow);
        if (request.Title is not null) update = update.Set(x => x.Title, request.Title.Trim());
        if (request.Body is not null)
        {
            var trimmed = request.Body.Trim();
            update = update.Set(x => x.Body, trimmed).Set(x => x.Excerpt, MakeExcerpt(trimmed));
        }
        if (request.BoardId is not null)
        {
            var resolved = ResolveBoard(request.BoardId);
            if (resolved is not null) update = update.Set(x => x.Board, resolved);
        }
        if (tagsResult.Tags is not null) update = update.Set(x => x.Tags, tagsResult.Tags);
        await _posts.UpdateOneAsync(x => x.Id == draftId, update);

        var updated = await _posts.Find(x => x.Id == draftId).FirstOrDefaultAsync();
        var names = await _authorNames.ResolveAsync(new[] { updated!.AuthorSubId });
        return Ok(ApiResult<PostDetailDto>.Ok(ForumDtoMapping.MapDetail(updated, names)));
    }

    // DELETE api/forum/posts/drafts/{draftId}
    [HttpDelete("{draftId}")]
    public async Task<ActionResult<ApiResult<object>>> DeleteDraft(string draftId)
    {
        var sub = GetSub();
        if (sub is null) return Unauthorized(ApiResult<object>.Fail("未授权", "UNAUTHORIZED"));

        var draft = await _posts.Find(x => x.Id == draftId && x.State == "draft").FirstOrDefaultAsync();
        if (draft is null) return NotFound(ApiResult<object>.Fail("未找到", "NOT_FOUND"));
        if (!string.Equals(draft.AuthorSubId, sub, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<object>.Fail("无权操作", "FORBIDDEN"));

        await _posts.DeleteOneAsync(x => x.Id == draftId);
        return Ok(ApiResult<object>.Ok(new object()));
    }

    // POST api/forum/posts/drafts/{draftId}/publish
    [HttpPost("{draftId}/publish")]
    public async Task<ActionResult<ApiResult<PublishDraftResultDto>>> PublishDraft(string draftId)
    {
        var sub = GetSub();
        if (sub is null) return Unauthorized(ApiResult<PublishDraftResultDto>.Fail("未授权", "UNAUTHORIZED"));

        var draft = await _posts.Find(x => x.Id == draftId && x.State == "draft").FirstOrDefaultAsync();
        if (draft is null) return NotFound(ApiResult<PublishDraftResultDto>.Fail("未找到", "NOT_FOUND"));
        if (!string.Equals(draft.AuthorSubId, sub, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<PublishDraftResultDto>.Fail("无权操作", "FORBIDDEN"));

        if (string.IsNullOrWhiteSpace(draft.Title))
            return BadRequest(ApiResult<PublishDraftResultDto>.Fail("标题不能为空", "INVALID_INPUT"));
        if (string.IsNullOrWhiteSpace(draft.Body))
            return BadRequest(ApiResult<PublishDraftResultDto>.Fail("正文不能为空", "INVALID_INPUT"));

        // Validate board
        var boardTitle = ResolveBoard(null, draft.Board);
        if (boardTitle is null)
            return BadRequest(ApiResult<PublishDraftResultDto>.Fail("无效的板块", "INVALID_BOARD_ID"));

        await _posts.UpdateOneAsync(x => x.Id == draftId,
            Builders<ForumPostRecord>.Update
                .Set(x => x.State, "published")
                .Set(x => x.Board, boardTitle));

        // Tags UseCount +1 for published tags in registry
        if (draft.Tags?.Count > 0)
            await _tags.UpdateManyAsync(
                Builders<ForumTagRecord>.Filter.In(x => x.Name, draft.Tags),
                Builders<ForumTagRecord>.Update.Inc(x => x.UseCount, 1));

        return Ok(ApiResult<PublishDraftResultDto>.Ok(new PublishDraftResultDto { Id = draftId, State = "published" }));
    }

    private string? GetSub()
    {
        try { return User.GetUserId(); }
        catch (UnauthorizedAccessException) { return null; }
    }

    private string? ResolveBoard(string? boardId, string? fallbackTitle = null)
    {
        if (!string.IsNullOrWhiteSpace(boardId))
        {
            var b = _boardOptions.Value.Boards.FirstOrDefault(x =>
                string.Equals(x.Id, boardId, StringComparison.OrdinalIgnoreCase));
            return b?.Title;
        }
        if (!string.IsNullOrWhiteSpace(fallbackTitle))
        {
            var b = _boardOptions.Value.Boards.FirstOrDefault(x =>
                string.Equals(x.Title, fallbackTitle, StringComparison.OrdinalIgnoreCase));
            return b?.Title;
        }
        return null;
    }

    private string DefaultBoard()
    {
        var g = _boardOptions.Value.Boards.FirstOrDefault(x =>
            x.Id.Equals("general", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(g?.Title)) return g.Title.Trim();
        var first = _boardOptions.Value.Boards.FirstOrDefault();
        return string.IsNullOrWhiteSpace(first?.Title) ? "综合" : first.Title.Trim();
    }

    private static string MakeExcerpt(string body, int maxLen = 200)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var t = body.Trim().Replace('\r', ' ').Replace('\n', ' ');
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t.Length <= maxLen ? t : t[..maxLen] + "…";
    }

    private static (List<string>? Tags, string? Error, string? Code) NormalizeTags(List<string>? tags)
    {
        if (tags is null || tags.Count == 0) return (null, null, null);
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var t = raw.Trim();
            if (t.Length > 32) return (null, "单个标签过长", "INVALID_TAGS");
            if (!seen.Add(t)) continue;
            if (list.Count >= 10) return (null, "标签数量过多", "INVALID_TAGS");
            list.Add(t);
        }
        return (list, null, null);
    }
}

public class CreateDraftRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? BoardId { get; set; }
    public List<string>? Tags { get; set; }
}

public class PublishDraftResultDto
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "published";
}
