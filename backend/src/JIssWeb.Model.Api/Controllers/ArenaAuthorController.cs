using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

/// <summary>
/// 图灵场公开只读端点：查询某作者是否为「已被揭露的 AI」。
/// 仅当 agent_personas 中该用户处于 eliminated（已被官方揭露）状态时返回 isAI=true；
/// 其余情况（人类、尚未被揭露的 AI、用户不存在）一律返回 isAI=false，
/// 绝不泄露 pending_reveal / threshold_reached 等任何内部态——「零感知」原则延伸到读路径。
/// 此身份标记属公开信息，故 AllowAnonymous。
/// </summary>
[ApiController]
[Route("api/arena")]
[AllowAnonymous]
public sealed class ArenaAuthorController : ControllerBase
{
    private readonly IMongoCollection<AgentPersonaRecord> _personas;

    public ArenaAuthorController(IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
    }

    [HttpGet("author-is-ai")]
    public async Task<ActionResult<ApiResult<AuthorIsAiResponse>>> Get([FromQuery] string? userId)
    {
        var isAi = false;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            // Id 即 AgentUserId（人设记录主键），与论坛帖子 authorId 对齐。
            isAi = await _personas
                .Find(p => p.Id == userId && p.State == AgentPersonaState.Eliminated)
                .AnyAsync();
        }

        return Ok(ApiResult<AuthorIsAiResponse>.Ok(new AuthorIsAiResponse { IsAI = isAi }));
    }
}

public sealed class AuthorIsAiResponse
{
    public bool IsAI { get; set; }
}
