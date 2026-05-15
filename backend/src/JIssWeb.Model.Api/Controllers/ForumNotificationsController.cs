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
[Route("api/forum/notifications")]
public class ForumNotificationsController : ControllerBase
{
    private readonly IMongoCollection<InAppNotificationRecord> _notifications;
    private readonly ForumAuthorDisplayResolver _authorNames;

    public ForumNotificationsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumAuthorDisplayResolver authorNames)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _notifications = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        _authorNames = authorNames;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResult<PagedNotificationsDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool unreadOnly = false)
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<PagedNotificationsDto>.Fail("未授权", "UNAUTHORIZED"));

        if (page < 1 || pageSize < 1 || pageSize > 50)
            return BadRequest(ApiResult<PagedNotificationsDto>.Fail("分页参数无效", "INVALID_PAGINATION"));

        var filter = Builders<InAppNotificationRecord>.Filter.Eq(x => x.RecipientSubId, sub);
        if (unreadOnly)
            filter = Builders<InAppNotificationRecord>.Filter.And(
                filter,
                Builders<InAppNotificationRecord>.Filter.Eq(x => x.ReadAtUtc, null));

        var total = await _notifications.CountDocumentsAsync(filter);
        var items = await _notifications.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        // For ReportResolved notifications, ActorSubId is "" (system) — skip resolution to avoid empty-sub lookups.
        var actorSubsToResolve = items
            .Where(x => x.Type != InAppNotificationTypes.ReportResolved)
            .Select(x => x.ActorSubId);
        var actorNames = await _authorNames.ResolveAsync(actorSubsToResolve);
        var dtos = items.Select(n => MapDto(n, actorNames)).ToList();
        return Ok(ApiResult<PagedNotificationsDto>.Ok(new PagedNotificationsDto
        {
            Items = dtos,
            TotalCount = (int)total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpGet("unread-count")]
    [Authorize]
    public async Task<ActionResult<ApiResult<UnreadCountDto>>> UnreadCount()
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<UnreadCountDto>.Fail("未授权", "UNAUTHORIZED"));

        var filter = Builders<InAppNotificationRecord>.Filter.And(
            Builders<InAppNotificationRecord>.Filter.Eq(x => x.RecipientSubId, sub),
            Builders<InAppNotificationRecord>.Filter.Eq(x => x.ReadAtUtc, null));
        var count = await _notifications.CountDocumentsAsync(filter);
        return Ok(ApiResult<UnreadCountDto>.Ok(new UnreadCountDto { Count = (int)count }));
    }

    [HttpPost("{id}/read")]
    [Authorize]
    public async Task<ActionResult<ApiResult<object>>> MarkRead(string id)
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<object>.Fail("未授权", "UNAUTHORIZED"));

        var now = DateTime.UtcNow;
        var ownedByCaller = Builders<InAppNotificationRecord>.Filter.And(
            Builders<InAppNotificationRecord>.Filter.Eq(x => x.Id, id),
            Builders<InAppNotificationRecord>.Filter.Eq(x => x.RecipientSubId, sub));

        var unreadOwnedByCaller = Builders<InAppNotificationRecord>.Filter.And(
            ownedByCaller,
            Builders<InAppNotificationRecord>.Filter.Eq(x => x.ReadAtUtc, null));

        var update = Builders<InAppNotificationRecord>.Update.Set(x => x.ReadAtUtc, now);
        var result = await _notifications.UpdateOneAsync(unreadOwnedByCaller, update);
        if (result.MatchedCount == 0)
        {
            var exists = await _notifications.Find(ownedByCaller).Project(x => x.Id).FirstOrDefaultAsync();
            if (exists is null)
                return NotFound(ApiResult<object>.Fail("未找到", "NOT_FOUND"));
        }

        return Ok(ApiResult<object>.Ok(new { }));
    }

    [HttpPost("read-all")]
    [Authorize]
    public async Task<ActionResult<ApiResult<object>>> MarkAllRead()
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<object>.Fail("未授权", "UNAUTHORIZED"));

        var now = DateTime.UtcNow;
        var filter = Builders<InAppNotificationRecord>.Filter.And(
            Builders<InAppNotificationRecord>.Filter.Eq(x => x.RecipientSubId, sub),
            Builders<InAppNotificationRecord>.Filter.Eq(x => x.ReadAtUtc, null));
        await _notifications.UpdateManyAsync(
            filter,
            Builders<InAppNotificationRecord>.Update.Set(x => x.ReadAtUtc, now));
        return Ok(ApiResult<object>.Ok(new { }));
    }

    private string? TryGetSub()
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

    private static NotificationDto MapDto(InAppNotificationRecord n, IReadOnlyDictionary<string, string> names)
    {
        var actorKey = n.ActorSubId ?? "";
        string actorDisplayName;
        if (n.Type == InAppNotificationTypes.ReportResolved)
        {
            actorDisplayName = "系统";
        }
        else
        {
            actorDisplayName = names.TryGetValue(actorKey, out var d) ? d : ForumDisplayName.ForSub(actorKey);
        }
        return new NotificationDto
        {
            Id = n.Id,
            Type = n.Type,
            PostId = n.PostId,
            ReplyId = n.ReplyId,
            ActorId = actorKey,
            ActorDisplayName = actorDisplayName,
            PostTitle = n.PostTitle,
            Read = n.ReadAtUtc.HasValue,
            CreatedAtUtc = n.CreatedAtUtc,
        };
    }
}

public class PagedNotificationsDto
{
    public List<NotificationDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class NotificationDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string PostId { get; set; } = "";
    public string? ReplyId { get; set; }
    public string ActorId { get; set; } = "";
    public string ActorDisplayName { get; set; } = "";
    public string PostTitle { get; set; } = "";
    public bool Read { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class UnreadCountDto
{
    public int Count { get; set; }
}
