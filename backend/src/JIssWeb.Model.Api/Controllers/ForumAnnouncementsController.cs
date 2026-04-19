using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/announcements")]
public class ForumAnnouncementsController : ControllerBase
{
    private readonly IMongoCollection<ForumAnnouncementRecord> _announcements;

    public ForumAnnouncementsController(IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _announcements = db.GetCollection<ForumAnnouncementRecord>(ForumMongoSetup.AnnouncementsCollectionName);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<List<ForumAnnouncementItemDto>>>> List([FromQuery] int limit = 5)
    {
        if (limit < 1 || limit > 50)
            return BadRequest(ApiResult<List<ForumAnnouncementItemDto>>.Fail("分页参数无效", "INVALID_PAGINATION"));

        Response.Headers.CacheControl = "public, max-age=60";

        var items = await _announcements.Find(FilterDefinition<ForumAnnouncementRecord>.Empty)
            .SortByDescending(x => x.Pinned)
            .ThenByDescending(x => x.PublishedAtUtc)
            .ThenBy(x => x.Id)
            .Limit(limit)
            .ToListAsync();

        var dtos = items.Select(a => new ForumAnnouncementItemDto
        {
            Id = a.Id,
            Title = a.Title,
            Summary = a.Summary,
            LinkUrl = a.LinkUrl,
            PublishedAtUtc = a.PublishedAtUtc,
            Pinned = a.Pinned,
        }).ToList();

        return Ok(ApiResult<List<ForumAnnouncementItemDto>>.Ok(dtos));
    }
}
