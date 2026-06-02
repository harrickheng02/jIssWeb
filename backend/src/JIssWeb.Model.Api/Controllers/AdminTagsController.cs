using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/admin/tags")]
[Authorize]
[RequireForumModerator]
public class AdminTagsController : ControllerBase
{
    private readonly IMongoCollection<ForumTagRecord> _tags;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IWebHostEnvironment _env;

    public AdminTagsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IWebHostEnvironment env)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _tags = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName); // seed-from-posts 仍需要
        _env = env;
    }

    private string? TryGetSub()
    {
        try { return User.GetUserId(); }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string ToSlug(string name) => name.Trim().ToLowerInvariant();

    private static ForumTagDto ToDto(ForumTagRecord r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Slug = r.Slug,
        Description = r.Description,
        Status = r.Status,
        UseCount = r.UseCount,
        CreatedAtUtc = r.CreatedAtUtc,
        UpdatedAtUtc = r.UpdatedAtUtc,
    };

    // GET /api/forum/admin/tags
    [HttpGet]
    public async Task<ActionResult<ApiResult<PagedTagsDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? q = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 50) pageSize = 50;

        var filters = new List<FilterDefinition<ForumTagRecord>>();

        if (!string.IsNullOrEmpty(status))
            filters.Add(Builders<ForumTagRecord>.Filter.Eq(x => x.Status, status));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var rx = new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q), "i");
            var nameMatch = Builders<ForumTagRecord>.Filter.Regex(x => x.Name, rx);
            var slugMatch = Builders<ForumTagRecord>.Filter.Regex(x => x.Slug, rx);
            filters.Add(Builders<ForumTagRecord>.Filter.Or(nameMatch, slugMatch));
        }

        var filter = filters.Count > 0
            ? Builders<ForumTagRecord>.Filter.And(filters)
            : Builders<ForumTagRecord>.Filter.Empty;

        var sort = Builders<ForumTagRecord>.Sort.Descending(x => x.UseCount);

        var totalCount = (int)await _tags.CountDocumentsAsync(filter);
        var items = await _tags.Find(filter)
            .Sort(sort)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return Ok(ApiResult<PagedTagsDto>.Ok(new PagedTagsDto
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        }));
    }

    // POST /api/forum/admin/tags
    [HttpPost]
    public async Task<ActionResult<ApiResult<ForumTagDto>>> Create([FromBody] CreateTagRequest request)
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<ForumTagDto>.Fail("未授权", "UNAUTHORIZED"));

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResult<ForumTagDto>.Fail("标签名称无效", "INVALID_TAG_NAME"));

        if (request.Name.Trim().Length > 32)
            return BadRequest(ApiResult<ForumTagDto>.Fail("标签名称过长", "INVALID_TAG_NAME"));

        var slug = ToSlug(request.Name);
        var now = DateTime.UtcNow;
        var record = new ForumTagRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description?.Trim(),
            Status = ForumTagStatuses.Active,
            UseCount = 0,
            CreatedAtUtc = now,
            CreatedBySub = sub,
        };

        try
        {
            await _tags.InsertOneAsync(record);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Conflict(ApiResult<ForumTagDto>.Fail("标签已存在", "TAG_SLUG_CONFLICT"));
        }

        return Ok(ApiResult<ForumTagDto>.Ok(ToDto(record)));
    }

    // PATCH /api/forum/admin/tags/{id}
    [HttpPatch("{id}")]
    public async Task<ActionResult<ApiResult<ForumTagDto>>> Edit(string id, [FromBody] EditTagRequest request)
    {
        var sub = TryGetSub();
        if (sub is null)
            return Unauthorized(ApiResult<ForumTagDto>.Fail("未授权", "UNAUTHORIZED"));

        var tag = await _tags.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (tag is null)
            return NotFound(ApiResult<ForumTagDto>.Fail("标签不存在", "TAG_NOT_FOUND"));

        var updates = new List<UpdateDefinition<ForumTagRecord>>();
        var now = DateTime.UtcNow;

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 32)
                return BadRequest(ApiResult<ForumTagDto>.Fail("标签名称无效", "INVALID_TAG_NAME"));

            var newSlug = ToSlug(request.Name);
            updates.Add(Builders<ForumTagRecord>.Update.Set(x => x.Name, request.Name.Trim()));
            updates.Add(Builders<ForumTagRecord>.Update.Set(x => x.Slug, newSlug));
        }

        if (request.Description is not null)
            updates.Add(Builders<ForumTagRecord>.Update.Set(x => x.Description, request.Description.Trim()));

        if (updates.Count == 0)
            return BadRequest(ApiResult<ForumTagDto>.Fail("未提供任何修改字段", "NO_FIELDS_TO_UPDATE"));

        updates.Add(Builders<ForumTagRecord>.Update.Set(x => x.UpdatedAtUtc, now));
        updates.Add(Builders<ForumTagRecord>.Update.Set(x => x.UpdatedBySub, sub));

        var combined = Builders<ForumTagRecord>.Update.Combine(updates);

        try
        {
            await _tags.UpdateOneAsync(x => x.Id == id, combined);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Conflict(ApiResult<ForumTagDto>.Fail("标签已存在", "TAG_SLUG_CONFLICT"));
        }

        var updated = await _tags.Find(x => x.Id == id).FirstOrDefaultAsync();
        return Ok(ApiResult<ForumTagDto>.Ok(ToDto(updated!)));
    }

    // POST /api/forum/admin/tags/{id}/disable
    [HttpPost("{id}/disable")]
    public async Task<ActionResult<ApiResult<ForumTagDto>>> Disable(string id)
    {
        var tag = await _tags.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (tag is null)
            return NotFound(ApiResult<ForumTagDto>.Fail("标签不存在", "TAG_NOT_FOUND"));

        if (tag.Status == ForumTagStatuses.Disabled)
            return Ok(ApiResult<ForumTagDto>.Ok(ToDto(tag)));

        await _tags.UpdateOneAsync(
            x => x.Id == id,
            Builders<ForumTagRecord>.Update.Set(x => x.Status, ForumTagStatuses.Disabled));

        var updated = await _tags.Find(x => x.Id == id).FirstOrDefaultAsync();
        return Ok(ApiResult<ForumTagDto>.Ok(ToDto(updated!)));
    }

    // POST /api/forum/admin/tags/{id}/enable
    [HttpPost("{id}/enable")]
    public async Task<ActionResult<ApiResult<ForumTagDto>>> Enable(string id)
    {
        var tag = await _tags.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (tag is null)
            return NotFound(ApiResult<ForumTagDto>.Fail("标签不存在", "TAG_NOT_FOUND"));

        if (tag.Status == ForumTagStatuses.Active)
            return Ok(ApiResult<ForumTagDto>.Ok(ToDto(tag)));

        await _tags.UpdateOneAsync(
            x => x.Id == id,
            Builders<ForumTagRecord>.Update.Set(x => x.Status, ForumTagStatuses.Active));

        var updated = await _tags.Find(x => x.Id == id).FirstOrDefaultAsync();
        return Ok(ApiResult<ForumTagDto>.Ok(ToDto(updated!)));
    }

    // DELETE /api/forum/admin/tags/{id}
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResult<object>>> Delete(string id)
    {
        var tag = await _tags.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (tag is null)
            return NotFound(ApiResult<object>.Fail("标签不存在", "TAG_NOT_FOUND"));

        await _tags.DeleteOneAsync(x => x.Id == id);
        return Ok(ApiResult<object>.Ok(new { message = "删除成功" }));
    }

    // POST /api/forum/admin/tags/seed-from-posts（仅非 Production）
    [HttpPost("seed-from-posts")]
    public async Task<ActionResult<ApiResult<object>>> SeedFromPosts()
    {
        if (_env.IsProduction())
            return NotFound();

        // 聚合所有帖子中的 Tags，统计每个 tag 的出现次数
        var pipeline = new[]
        {
            new BsonDocument("$unwind", "$Tags"),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$Tags" },
                { "count", new BsonDocument("$sum", 1) }
            })
        };

        var tagCounts = await _posts
            .AggregateAsync<BsonDocument>(pipeline);

        var tagCountList = await tagCounts.ToListAsync();

        if (tagCountList.Count == 0)
            return Ok(ApiResult<object>.Ok(new { upserted = 0 }));

        var now = DateTime.UtcNow;
        var bulkOps = new List<WriteModel<ForumTagRecord>>();

        foreach (var doc in tagCountList)
        {
            var name = doc["_id"].AsString;
            var count = doc["count"].AsInt32;
            var slug = ToSlug(name);

            // upsert：若 slug 不存在则插入，已存在的不覆盖
            var filterDef = Builders<ForumTagRecord>.Filter.Eq(x => x.Slug, slug);
            var updateDef = Builders<ForumTagRecord>.Update
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.Name, name)
                .SetOnInsert(x => x.Slug, slug)
                .SetOnInsert(x => x.Status, ForumTagStatuses.Active)
                .SetOnInsert(x => x.UseCount, count)
                .SetOnInsert(x => x.CreatedAtUtc, now)
                .SetOnInsert(x => x.CreatedBySub, "seed");

            bulkOps.Add(new UpdateOneModel<ForumTagRecord>(filterDef, updateDef) { IsUpsert = true });
        }

        var result = await _tags.BulkWriteAsync(bulkOps);
        return Ok(ApiResult<object>.Ok(new { upserted = result.Upserts.Count }));
    }
}

// DTOs
public class ForumTagDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public int UseCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class PagedTagsDto
{
    public List<ForumTagDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class CreateTagRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class EditTagRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

