using System.Collections.Generic;
using System.Text.RegularExpressions;
using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Middleware;
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
    private readonly IMongoCollection<ForumTagRecord> _tags;
    private readonly IMongoCollection<ForumPostLikeRecord> _likes;
    private readonly IMongoCollection<ForumPostFavoriteRecord> _favorites;
    private readonly IOptions<ForumBoardsOptions> _boardOptions;
    private readonly ForumAuthorDisplayResolver _authorNames;
    private readonly ForumEngagementService _engagement;
    private readonly ForumModerationDeleteService _moderationDelete;
    private readonly IForumBlockedWordFilter _blockedWords;
    private readonly IForumPostRateLimitService _postRateLimit;
    private readonly IForumNotificationWriter _notificationWriter;
    private readonly ILogger<ForumPostsController> _logger;

    public ForumPostsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boardOptions,
        ForumAuthorDisplayResolver authorNames,
        ForumEngagementService engagement,
        ForumModerationDeleteService moderationDelete,
        IForumBlockedWordFilter blockedWords,
        IForumPostRateLimitService postRateLimit,
        IForumNotificationWriter notificationWriter,
        ILogger<ForumPostsController> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _tags = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
        _likes = db.GetCollection<ForumPostLikeRecord>(ForumMongoSetup.LikesCollectionName);
        _favorites = db.GetCollection<ForumPostFavoriteRecord>(ForumMongoSetup.FavoritesCollectionName);
        _boardOptions = boardOptions;
        _authorNames = authorNames;
        _engagement = engagement;
        _moderationDelete = moderationDelete;
        _blockedWords = blockedWords;
        _postRateLimit = postRateLimit;
        _notificationWriter = notificationWriter;
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

        bool? featuredFilter = null;
        if (Request.Query.TryGetValue("featured", out var featuredVals))
        {
            var ft = featuredVals.ToString().Trim();
            if (!bool.TryParse(ft, out var fv))
                return BadRequest(ApiResult<PagedPostsDto>.Fail("featured 参数无效", "INVALID_FEATURED"));
            featuredFilter = fv;
        }

        var parts = new List<FilterDefinition<ForumPostRecord>>();
        parts.Add(ForumPostFilters.Published());
        if (boardFilter is not null) parts.Add(boardFilter);
        if (searchFilter is not null) parts.Add(searchFilter);
        if (tagFilter is not null) parts.Add(tagFilter);
        if (featuredFilter is not null)
            parts.Add(Builders<ForumPostRecord>.Filter.Eq(x => x.IsFeatured, featuredFilter.Value));
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
        var useFeaturedSort = featuredFilter == true && searchFilter is null;

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

        var total = await _posts.CountDocumentsAsync(filter);
        var find = _posts.Find(filter);
        var sortDef = BuildPostListSortDefinition(useHotSort, keywordSearchActive, useFeaturedSort);
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

        if (post.State == "deleted")
        {
            var requesterId = TryGetAuthorId();
            var isAuthorSelfDelete = string.Equals(post.DeletedBySub, post.AuthorSubId, StringComparison.Ordinal);
            var isAuthor = string.Equals(post.AuthorSubId, requesterId, StringComparison.Ordinal);
            var isMod = IsModeratorOrAdmin();
            if (!((isAuthorSelfDelete && isAuthor) || isMod))
                return NotFound(ApiResult<List<ReplyDto>>.Fail("未找到", "NOT_FOUND"));
        }

        var replyFilter = Builders<ForumReplyRecord>.Filter.And(
            ForumPostFilters.ReplyPublished(),
            Builders<ForumReplyRecord>.Filter.Eq(x => x.PostId, postId));
        var list = await _replies.Find(replyFilter)
            .SortBy(x => x.CreatedAtUtc)
            .ToListAsync();
        var replyNames = await _authorNames.ResolveAsync(list.Select(x => x.AuthorSubId));
        return Ok(ApiResult<List<ReplyDto>>.Ok(list.Select(r => ForumDtoMapping.ToReplyDto(r, replyNames)).ToList()));
    }

    [HttpPost("{postId}/replies")]
    [Authorize]
    [BlockForumMuted]
    public async Task<ActionResult<ApiResult<ReplyDto>>> CreateReply(string postId, [FromBody] CreateReplyRequest request)
    {
        var authorId = TryGetAuthorId();
        if (authorId is null)
            return Unauthorized(ApiResult<ReplyDto>.Fail("未授权", "UNAUTHORIZED"));

        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(ApiResult<ReplyDto>.Fail("内容不能为空", "INVALID_INPUT"));

        var isAgent = IsAgentAccount();
        var blockedEvaluation = isAgent ? BlockedWordEvaluation.Pass : _blockedWords.Evaluate(null, request.Body);
        if (blockedEvaluation == BlockedWordEvaluation.Reject)
            return BadRequest(ApiResult<ReplyDto>.Fail("内容包含不允许的词汇", "BLOCKED_CONTENT"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null)
            return NotFound(ApiResult<ReplyDto>.Fail("未找到", "NOT_FOUND"));

        if (post.RepliesLocked)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<ReplyDto>.Fail("本帖已禁止回复", "REPLIES_LOCKED"));

        if (blockedEvaluation == BlockedWordEvaluation.Local)
        {
            var now = DateTime.UtcNow;
            var localNames = await _authorNames.ResolveAsync(new[] { authorId });
            return Ok(ApiResult<ReplyDto>.Ok(new ReplyDto
            {
                Id = CreateLocalId(),
                PostId = postId,
                AuthorId = authorId,
                AuthorDisplayName = ForumDtoMapping.DisplayNameFor(localNames, authorId),
                Body = request.Body.Trim(),
                State = ForumContentStates.Local,
                LocalOnly = true,
                CreatedAtUtc = now,
            }));
        }

        if (await _postRateLimit.IsReplyCreateRateLimitedAsync(authorId, GetClientIp(), isAgent))
            return StatusCode(StatusCodes.Status429TooManyRequests, ApiResult<ReplyDto>.Fail("请求过于频繁", "RATE_LIMITED"));

        var reply = new ForumReplyRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            PostId = postId,
            AuthorSubId = authorId,
            Body = request.Body.Trim(),
            State = ForumContentStates.Published,
            CreatedAtUtc = DateTime.UtcNow,
        };
        await _replies.InsertOneAsync(reply);
        await _postRateLimit.RecordSuccessfulReplyCreateAsync(authorId, GetClientIp(), isAgent);
        await _posts.UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update.Inc(x => x.CommentCount, 1));

        await _notificationWriter.WriteReplyNotificationAsync(post, reply);

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

        // draft 只有作者本人可见；deleted 仅作者自删时本人可见，或版主/管理员可见
        var requesterId = TryGetAuthorId();
        bool incrementView = true;
        if (post.State == "deleted")
        {
            var isAuthorSelfDelete = string.Equals(post.DeletedBySub, post.AuthorSubId, StringComparison.Ordinal);
            var isAuthor = string.Equals(post.AuthorSubId, requesterId, StringComparison.Ordinal);
            var isMod = IsModeratorOrAdmin();
            if ((isAuthorSelfDelete && isAuthor) || isMod)
                incrementView = false; // 已删帖不计浏览
            else
                return NotFound(ApiResult<PostDetailDto>.Fail("未找到", "NOT_FOUND"));
        }
        else if (post.State == "draft")
        {
            if (!string.Equals(post.AuthorSubId, requesterId, StringComparison.Ordinal))
                return NotFound(ApiResult<PostDetailDto>.Fail("未找到", "NOT_FOUND"));
            incrementView = false;
        }

        if (incrementView)
        {
            await _posts.UpdateOneAsync(x => x.Id == id, Builders<ForumPostRecord>.Update.Inc(x => x.ViewCount, 1));
            post.ViewCount += 1;
        }

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

    [HttpPut("{postId}/replies/{replyId}")]
    [Authorize]
    [BlockForumMuted]
    public async Task<ActionResult<ApiResult<ReplyDto>>> UpdateReply(string postId, string replyId, [FromBody] UpdateReplyRequest request)
    {
        var authorId = TryGetAuthorId();
        if (authorId is null)
            return Unauthorized(ApiResult<ReplyDto>.Fail("未授权", "UNAUTHORIZED"));

        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(ApiResult<ReplyDto>.Fail("内容不能为空", "INVALID_INPUT"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null || post.State == "deleted")
            return NotFound(ApiResult<ReplyDto>.Fail("未找到", "NOT_FOUND"));

        var reply = await _replies.Find(x => x.Id == replyId && x.PostId == postId).FirstOrDefaultAsync();
        if (reply is null || reply.State == "deleted")
            return NotFound(ApiResult<ReplyDto>.Fail("未找到", "NOT_FOUND"));

        if (!string.Equals(reply.AuthorSubId, authorId, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<ReplyDto>.Fail("无权编辑", "FORBIDDEN"));

        var isAgent = IsAgentAccount();
        // Blocked word check — PUT always rejects regardless of Handling (agents exempt)
        if (!isAgent && _blockedWords.Evaluate(null, request.Body) != BlockedWordEvaluation.Pass)
            return BadRequest(ApiResult<ReplyDto>.Fail("内容含有违禁词汇", "BLOCKED_CONTENT"));

        var now = DateTime.UtcNow;
        await _replies.UpdateOneAsync(
            x => x.Id == replyId,
            Builders<ForumReplyRecord>.Update
                .Set(x => x.Body, request.Body.Trim())
                .Set(x => x.UpdatedAtUtc, now));

        var updated = await _replies.Find(x => x.Id == replyId).FirstOrDefaultAsync();
        var names = await _authorNames.ResolveAsync(new[] { updated!.AuthorSubId });
        return Ok(ApiResult<ReplyDto>.Ok(ForumDtoMapping.ToReplyDto(updated, names)));
    }

    [HttpDelete("{postId}")]
    [Authorize]
    public async Task<ActionResult<ApiResult<string>>> DeletePost(string postId)
    {
        var sub = TryGetAuthorId();
        if (sub is null)
            return Unauthorized(ApiResult<string>.Fail("未授权", "UNAUTHORIZED"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null || post.State == "deleted")
            return NotFound(ApiResult<string>.Fail("未找到", "NOT_FOUND"));

        if (post.State == "draft")
            return BadRequest(ApiResult<string>.Fail("请先删除草稿", "DRAFT_MUST_BE_DELETED_DIRECTLY"));

        if (!string.Equals(post.AuthorSubId, sub, StringComparison.Ordinal))
        {
            if (IsModeratorOrAdmin())
            {
                var modOutcome = await _moderationDelete.TryDeletePostAsync(User, sub, postId);
                if (modOutcome == ModerationDeletionOutcome.Success)
                    return Ok(ApiResult<string>.Ok("已删除"));
                if (modOutcome == ModerationDeletionOutcome.NotFound)
                    return NotFound(ApiResult<string>.Fail("未找到", "NOT_FOUND"));
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权删除", "FORBIDDEN"));
            }
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权删除", "FORBIDDEN"));
        }

        var now = DateTime.UtcNow;
        await _posts.UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update
            .Set(x => x.State, "deleted")
            .Set(x => x.DeletedAtUtc, now)
            .Set(x => x.DeletedBySub, sub));

        // Tags UseCount -1
        if (post.Tags.Count > 0)
            await ApplyTagsDeltaAsync(post.Tags, new List<string>());

        return Ok(ApiResult<string>.Ok("已删除"));
    }

    [HttpDelete("{postId}/replies/{replyId}")]
    [Authorize]
    public async Task<ActionResult<ApiResult<string>>> DeleteReply(string postId, string replyId)
    {
        var sub = TryGetAuthorId();
        if (sub is null)
            return Unauthorized(ApiResult<string>.Fail("未授权", "UNAUTHORIZED"));

        // 查父帖（不要求 published，但不能是 deleted 状态的版主删帖；对作者自删的帖子也允许作者删其下回复）
        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null)
            return NotFound(ApiResult<string>.Fail("未找到", "NOT_FOUND"));

        var reply = await _replies.Find(x => x.Id == replyId && x.PostId == postId).FirstOrDefaultAsync();
        if (reply is null || reply.State == "deleted")
            return NotFound(ApiResult<string>.Fail("未找到", "NOT_FOUND"));

        if (!string.Equals(reply.AuthorSubId, sub, StringComparison.Ordinal))
        {
            if (IsModeratorOrAdmin())
            {
                var modOutcome = await _moderationDelete.TryDeleteReplyAsync(User, sub, replyId);
                if (modOutcome == ModerationDeletionOutcome.Success)
                    return Ok(ApiResult<string>.Ok("已删除"));
                if (modOutcome == ModerationDeletionOutcome.NotFound)
                    return NotFound(ApiResult<string>.Fail("未找到", "NOT_FOUND"));
                return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权删除", "FORBIDDEN"));
            }
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权删除", "FORBIDDEN"));
        }

        var now = DateTime.UtcNow;
        await _replies.UpdateOneAsync(x => x.Id == replyId, Builders<ForumReplyRecord>.Update
            .Set(x => x.State, "deleted")
            .Set(x => x.DeletedAtUtc, now)
            .Set(x => x.DeletedBySub, sub));

        await _posts.UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update.Inc(x => x.CommentCount, -1));

        return Ok(ApiResult<string>.Ok("已删除"));
    }

    [HttpDelete("{postId}/permanent")]
    [Authorize]
    public async Task<ActionResult<ApiResult<string>>> PermanentDeletePost(string postId)
    {
        var sub = TryGetAuthorId();
        if (sub is null)
            return Unauthorized(ApiResult<string>.Fail("未授权", "UNAUTHORIZED"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null || post.State != "deleted")
            return NotFound(ApiResult<string>.Fail("未找到", "NOT_FOUND"));

        // 只有作者自删的帖子，作者才能永久删除
        var isAuthorSelfDelete = string.Equals(post.DeletedBySub, post.AuthorSubId, StringComparison.Ordinal);
        if (!isAuthorSelfDelete || !string.Equals(post.AuthorSubId, sub, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<string>.Fail("无权永久删除", "FORBIDDEN"));

        // 检查 engagement
        if (post.LikeCount > 0 || post.CommentCount > 0)
            return BadRequest(ApiResult<string>.Fail("帖子已有互动，无法永久删除", "HAS_ENGAGEMENT"));

        var favCount = await _favorites.CountDocumentsAsync(f => f.PostId == postId);
        if (favCount > 0)
            return BadRequest(ApiResult<string>.Fail("帖子已有互动，无法永久删除", "HAS_ENGAGEMENT"));

        // 物理删除：级联清理
        await _replies.DeleteManyAsync(x => x.PostId == postId);
        await _notificationWriter.DeleteByPostAsync(postId);
        await _likes.DeleteManyAsync(x => x.PostId == postId);
        await _favorites.DeleteManyAsync(x => x.PostId == postId);
        await _posts.DeleteOneAsync(x => x.Id == postId);

        return Ok(ApiResult<string>.Ok("已永久删除"));
    }

    [HttpPut("{postId}")]
    [Authorize]
    [BlockForumMuted]
    public async Task<ActionResult<ApiResult<PostListItemDto>>> UpdatePost(string postId, [FromBody] UpdatePostRequest request)
    {
        var authorId = TryGetAuthorId();
        if (authorId is null)
            return Unauthorized(ApiResult<PostListItemDto>.Fail("未授权", "UNAUTHORIZED"));

        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null || post.State == "deleted")
            return NotFound(ApiResult<PostListItemDto>.Fail("未找到", "NOT_FOUND"));

        if (!string.Equals(post.AuthorSubId, authorId, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<PostListItemDto>.Fail("无权编辑", "FORBIDDEN"));

        if (post.State == "published"
            && (!string.IsNullOrWhiteSpace(request.BoardId) || !string.IsNullOrWhiteSpace(request.Board)))
            return BadRequest(ApiResult<PostListItemDto>.Fail("已发布帖子不可更换板块", "BOARD_NOT_EDITABLE"));

        // Tags normalization & validation
        List<string>? newTags = null;
        if (request.Tags is not null)
        {
            var tagsResult = NormalizeCreateTags(request.Tags);
            if (tagsResult.Error is not null)
                return BadRequest(ApiResult<PostListItemDto>.Fail(tagsResult.Error, tagsResult.Code));
            newTags = tagsResult.Tags;
        }

        // Blocked word check — evaluate only submitted fields; PUT always rejects regardless of Handling (agents exempt)
        var isAgent = IsAgentAccount();
        var editTitle = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : null;
        var editBody = !string.IsNullOrWhiteSpace(request.Body) ? request.Body : null;
        if (!isAgent
            && (editTitle is not null || editBody is not null)
            && _blockedWords.Evaluate(editTitle, editBody) != BlockedWordEvaluation.Pass)
            return BadRequest(ApiResult<PostListItemDto>.Fail("内容含有违禁词汇", "BLOCKED_CONTENT"));

        var now = DateTime.UtcNow;
        var updateDef = Builders<ForumPostRecord>.Update.Set(x => x.UpdatedAtUtc, now);

        if (!string.IsNullOrWhiteSpace(request.Title))
            updateDef = updateDef.Set(x => x.Title, request.Title.Trim());

        if (!string.IsNullOrWhiteSpace(request.Body))
        {
            var newBody = request.Body.Trim();
            updateDef = updateDef
                .Set(x => x.Body, newBody)
                .Set(x => x.Excerpt, MakeExcerpt(newBody));
        }

        if (newTags is not null)
            updateDef = updateDef.Set(x => x.Tags, newTags);

        await _posts.UpdateOneAsync(x => x.Id == postId, updateDef);

        // Tags UseCount delta update
        if (newTags is not null)
            await ApplyTagsDeltaAsync(post.Tags, newTags);

        // Reload for response
        var updated = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        var names = await _authorNames.ResolveAsync(new[] { updated!.AuthorSubId });
        return Ok(ApiResult<PostListItemDto>.Ok(ForumDtoMapping.ToListItem(updated, names)));
    }

    private async Task ApplyTagsDeltaAsync(List<string> oldTags, List<string> newTags)
    {
        var oldSet = new HashSet<string>(oldTags, StringComparer.OrdinalIgnoreCase);
        var newSet = new HashSet<string>(newTags, StringComparer.OrdinalIgnoreCase);

        var removed = oldTags.Where(t => !newSet.Contains(t)).ToList();
        var added = newTags.Where(t => !oldSet.Contains(t)).ToList();

        if (removed.Count > 0)
            await _tags.UpdateManyAsync(
                Builders<ForumTagRecord>.Filter.And(
                    Builders<ForumTagRecord>.Filter.In(x => x.Name, removed),
                    Builders<ForumTagRecord>.Filter.Gt(x => x.UseCount, 0)),
                Builders<ForumTagRecord>.Update.Inc(x => x.UseCount, -1));

        if (added.Count > 0)
            await _tags.UpdateManyAsync(
                Builders<ForumTagRecord>.Filter.In(x => x.Name, added),
                Builders<ForumTagRecord>.Update.Inc(x => x.UseCount, 1));
    }

    [HttpPost]
    [Authorize]
    [BlockForumMuted]
    public async Task<ActionResult<ApiResult<CreatePostResultDto>>> Create([FromBody] CreatePostRequest request)
    {
        var authorId = TryGetAuthorId();
        if (authorId is null)
            return Unauthorized(ApiResult<CreatePostResultDto>.Fail("未授权", "UNAUTHORIZED"));

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(ApiResult<CreatePostResultDto>.Fail("标题不能为空", "INVALID_INPUT"));
        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(ApiResult<CreatePostResultDto>.Fail("正文不能为空", "INVALID_INPUT"));

        var isAgent = IsAgentAccount();
        var blockedEvaluation = isAgent ? BlockedWordEvaluation.Pass : _blockedWords.Evaluate(request.Title, request.Body);
        if (blockedEvaluation == BlockedWordEvaluation.Reject)
            return BadRequest(ApiResult<CreatePostResultDto>.Fail("内容包含不允许的词汇", "BLOCKED_CONTENT"));

        var boardResolved = TryResolveBoardForCreate(request);
        if (boardResolved.Error is not null)
            return BadRequest(ApiResult<CreatePostResultDto>.Fail(boardResolved.Error, boardResolved.Code));

        var tagsResult = NormalizeCreateTags(request.Tags);
        if (tagsResult.Error is not null)
            return BadRequest(ApiResult<CreatePostResultDto>.Fail(tagsResult.Error, tagsResult.Code));

        if (blockedEvaluation == BlockedWordEvaluation.Local)
        {
            var localNames = await _authorNames.ResolveAsync(new[] { authorId });
            return Ok(ApiResult<CreatePostResultDto>.Ok(new CreatePostResultDto
            {
                Id = CreateLocalId(),
                State = ForumContentStates.Local,
                LocalOnly = true,
                AuthorDisplayName = ForumDtoMapping.DisplayNameFor(localNames, authorId),
            }));
        }

        if (await _postRateLimit.IsPostCreateRateLimitedAsync(authorId, GetClientIp(), isAgent))
            return StatusCode(StatusCodes.Status429TooManyRequests, ApiResult<CreatePostResultDto>.Fail("请求过于频繁", "RATE_LIMITED"));

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
        await _postRateLimit.RecordSuccessfulPostCreateAsync(authorId, GetClientIp(), isAgent);

        // 对已在注册表中的标签同步 UseCount（混合模式：非强制校验，仅跟踪已注册标签）
        if (tagsResult.Tags!.Count > 0)
            await _tags.UpdateManyAsync(
                Builders<ForumTagRecord>.Filter.In(x => x.Name, tagsResult.Tags!),
                Builders<ForumTagRecord>.Update.Inc(x => x.UseCount, 1));

        return Ok(ApiResult<CreatePostResultDto>.Ok(new CreatePostResultDto { Id = doc.Id, State = ForumContentStates.Published }));
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
    /// Featured feed (<c>useFeaturedSort=true</c>): ordered by <see cref="ForumPostRecord.FeaturedAtUtc"/> descending (null falls back to <see cref="ForumPostRecord.CreatedAtUtc"/>); overrides sort param.
    /// </summary>
    private static SortDefinition<ForumPostRecord> BuildPostListSortDefinition(bool useHotSort, bool keywordSearchActive, bool useFeaturedSort = false)
    {
        if (keywordSearchActive)
        {
            return Builders<ForumPostRecord>.Sort
                .Descending(x => x.CreatedAtUtc)
                .Ascending(x => x.Id);
        }

        if (useFeaturedSort)
        {
            // FeaturedAtUtc desc, with null values falling to the end via CreatedAtUtc desc tiebreak
            return Builders<ForumPostRecord>.Sort
                .Descending(x => x.FeaturedAtUtc)
                .Descending(x => x.CreatedAtUtc);
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

    private string GetClientIp() => ForumRateLimitHttpHelpers.GetClientIp(HttpContext);

    private bool IsAgentAccount() => ForumRateLimitHttpHelpers.IsAgentAccount(HttpContext);

    private static string CreateLocalId() => $"local:{Guid.NewGuid():N}";

    private bool IsModeratorOrAdmin()
    {
        try
        {
            var role = User.GetForumPrincipalRole();
            return role == ForumPrincipalRole.Moderator || role == ForumPrincipalRole.Admin;
        }
        catch
        {
            return false;
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
    public bool IsFeatured { get; set; }
    public int Likes { get; set; }
    public int FavoriteCount { get; set; }
    public int Comments { get; set; }
    public int Views { get; set; }
    public bool LikedByMe { get; set; }
    public bool FavoritedByMe { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string State { get; set; } = "published";
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBySub { get; set; }
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
    public string State { get; set; } = ForumContentStates.Published;
    public bool LocalOnly { get; set; }
    public string? AuthorDisplayName { get; set; }
}

public class CreateReplyRequest
{
    public string? Body { get; set; }
}

public class UpdatePostRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? BoardId { get; set; }
    public string? Board { get; set; }
    public List<string>? Tags { get; set; }
}

public class UpdateReplyRequest
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
    public string State { get; set; } = ForumContentStates.Published;
    public bool LocalOnly { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
