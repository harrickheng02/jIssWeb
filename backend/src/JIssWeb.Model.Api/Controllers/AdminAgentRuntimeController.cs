using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController, Route("api/forum/admin/agent-runtime"), Authorize, RequireForumModerator]
public sealed class AdminAgentRuntimeController : ControllerBase
{
    private readonly IMongoCollection<AgentRuntimeConfigRecord> _configs;
    private readonly IAgentLlmConfigResolver _resolver;

    public AdminAgentRuntimeController(IMongoClient client, IOptions<MongoSettings> mongo, IAgentLlmConfigResolver resolver)
    {
        _configs = client.GetDatabase(mongo.Value.DatabaseName).GetCollection<AgentRuntimeConfigRecord>(AgentMongoSetup.RuntimeConfigCollectionName);
        _resolver = resolver;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<AgentRuntimeConfigDto>>> Get(CancellationToken ct)
    {
        var record = await _resolver.GetRuntimeAsync(ct);
        return Ok(ApiResult<AgentRuntimeConfigDto>.Ok(ToDto(record)));
    }

    [HttpPut]
    public async Task<ActionResult<ApiResult<AgentRuntimeConfigDto>>> Put([FromBody] UpdateAgentRuntimeConfigRequest request, CancellationToken ct)
    {
        var current = await _resolver.GetRuntimeAsync(ct) ?? new AgentRuntimeConfigRecord();
        if (request.SchedulerEnabled is not null)
            current.SchedulerEnabled = request.SchedulerEnabled.Value;
        current.UpdatedAtUtc = DateTime.UtcNow;
        await _configs.ReplaceOneAsync(x => x.Id == current.Id, current, new ReplaceOptions { IsUpsert = true }, ct);
        return Ok(ApiResult<AgentRuntimeConfigDto>.Ok(ToDto(current)));
    }

    private static AgentRuntimeConfigDto ToDto(AgentRuntimeConfigRecord? record)
        => new() { SchedulerEnabled = record?.SchedulerEnabled ?? false };
}

public sealed class UpdateAgentRuntimeConfigRequest
{
    public bool? SchedulerEnabled { get; set; }
}

public sealed class AgentRuntimeConfigDto
{
    public bool SchedulerEnabled { get; set; }
}
