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
[Route("api/forum/tags")]
public class ForumTagsController : ControllerBase
{
    private readonly IMongoCollection<ForumTagRecord> _tags;

    public ForumTagsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _tags = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
    }

    [HttpGet("popular")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<List<string>>>> Popular(
        [FromQuery] string? boardId = null,
        [FromQuery] int limit = 20)
    {
        if (limit < 1 || limit > 50)
            return BadRequest(ApiResult<List<string>>.Fail("分页参数无效", "INVALID_PAGINATION"));

        // boardId 参数保留以兼容旧调用，但注册表是全站级别，不按版块过滤
        var filter = Builders<ForumTagRecord>.Filter.Eq(x => x.Status, ForumTagStatuses.Active);
        var sort = Builders<ForumTagRecord>.Sort
            .Descending(x => x.UseCount)
            .Ascending(x => x.Slug);

        var docs = await _tags.Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync();

        var names = docs.Select(d => d.Name).ToList();
        return Ok(ApiResult<List<string>>.Ok(names));
    }

    [HttpGet("suggest")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<List<string>>>> Suggest(
        [FromQuery] string? q = null,
        [FromQuery] int limit = 10)
    {
        if (limit < 1 || limit > 50)
            return BadRequest(ApiResult<List<string>>.Fail("分页参数无效", "INVALID_PAGINATION"));

        var filters = new List<FilterDefinition<ForumTagRecord>>
        {
            Builders<ForumTagRecord>.Filter.Eq(x => x.Status, ForumTagStatuses.Active)
        };

        if (!string.IsNullOrEmpty(q))
        {
            var rx = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q), "i");
            var nameMatch = Builders<ForumTagRecord>.Filter.Regex(x => x.Name, rx);
            var slugMatch = Builders<ForumTagRecord>.Filter.Regex(x => x.Slug, rx);
            filters.Add(Builders<ForumTagRecord>.Filter.Or(nameMatch, slugMatch));
        }

        var filter = Builders<ForumTagRecord>.Filter.And(filters);
        var sort = Builders<ForumTagRecord>.Sort
            .Descending(x => x.UseCount)
            .Ascending(x => x.Name);

        var docs = await _tags.Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync();

        var names = docs.Select(d => d.Name).ToList();
        return Ok(ApiResult<List<string>>.Ok(names));
    }
}
