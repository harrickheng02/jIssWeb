using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/tags")]
public class ForumTagsController : ControllerBase
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IOptions<ForumBoardsOptions> _boardOptions;

    public ForumTagsController(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ForumBoardsOptions> boardOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _boardOptions = boardOptions;
    }

    [HttpGet("popular")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<List<string>>>> Popular(
        [FromQuery] string? boardId = null,
        [FromQuery] int limit = 20)
    {
        if (limit < 1 || limit > 50)
            return BadRequest(ApiResult<List<string>>.Fail("分页参数无效", "INVALID_PAGINATION"));

        string? boardTitle = null;
        if (!string.IsNullOrWhiteSpace(boardId))
        {
            boardTitle = ForumBoardIdLookup.ResolveConfiguredBoardTitle(_boardOptions.Value, boardId);
            if (boardTitle is null)
                return BadRequest(ApiResult<List<string>>.Fail("无效的板块", "INVALID_BOARD_ID"));
        }

        var stages = new List<BsonDocument>();
        if (boardTitle is not null)
            stages.Add(new BsonDocument("$match", new BsonDocument("Board", boardTitle)));
        stages.Add(new BsonDocument("$unwind", "$Tags"));
        stages.Add(new BsonDocument("$match", new BsonDocument("Tags", new BsonDocument("$nin", new BsonArray { "", BsonNull.Value }))));
        stages.Add(new BsonDocument("$group", new BsonDocument
        {
            ["_id"] = new BsonDocument("$toLower", "$Tags"),
            ["count"] = new BsonDocument("$sum", 1),
            ["canonical"] = new BsonDocument("$first", "$Tags"),
        }));
        stages.Add(new BsonDocument("$sort", new BsonDocument { ["count"] = -1, ["_id"] = 1 }));
        stages.Add(new BsonDocument("$limit", limit));
        stages.Add(new BsonDocument("$project", new BsonDocument { ["_id"] = 0, ["tag"] = "$canonical" }));

        var pipeline = PipelineDefinition<ForumPostRecord, BsonDocument>.Create(stages);
        var docs = await _posts.Aggregate(pipeline).ToListAsync();
        var tags = docs.Select(d => d.GetValue("tag").AsString).ToList();
        return Ok(ApiResult<List<string>>.Ok(tags));
    }
}
