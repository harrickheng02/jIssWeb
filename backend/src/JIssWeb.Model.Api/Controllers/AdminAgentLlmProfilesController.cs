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

[ApiController, Route("api/forum/admin/agent-llm-profiles"), Authorize, RequireForumModerator]
public sealed class AdminAgentLlmProfilesController : ControllerBase
{
    private readonly IMongoCollection<AgentLlmProfileRecord> _profiles;
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly ILlmChatClient _llm;

    public AdminAgentLlmProfilesController(IMongoClient client, IOptions<MongoSettings> mongo, ILlmChatClient llm)
    {
        var db = client.GetDatabase(mongo.Value.DatabaseName);
        _profiles = db.GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _llm = llm;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<List<AgentLlmProfileDto>>>> List(CancellationToken ct)
    {
        var items = await _profiles.Find(FilterDefinition<AgentLlmProfileRecord>.Empty)
            .SortBy(x => x.Name)
            .ToListAsync(ct);
        return Ok(ApiResult<List<AgentLlmProfileDto>>.Ok(items.Select(x => ToDto(x)).ToList()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<AgentLlmProfileDto>>> Get(string id, CancellationToken ct)
    {
        var profile = await Find(id).FirstOrDefaultAsync(ct);
        if (profile is null)
            return NotFound(ApiResult<AgentLlmProfileDto>.Fail("LLM 档案不存在", "LLM_PROFILE_NOT_FOUND"));
        return Ok(ApiResult<AgentLlmProfileDto>.Ok(ToDto(profile)));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<AgentLlmProfileDto>>> Create([FromBody] UpsertAgentLlmProfileRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        var baseUrl = request.BaseUrl?.Trim() ?? "";
        var model = request.Model?.Trim() ?? "";
        var apiKey = request.ApiKey?.Trim() ?? "";
        if (name.Length == 0 || baseUrl.Length == 0 || model.Length == 0 || apiKey.Length == 0)
            return BadRequest(ApiResult<AgentLlmProfileDto>.Fail("名称、BaseUrl、模型与 ApiKey 均必填", "INVALID_INPUT"));

        var now = DateTime.UtcNow;
        var record = new AgentLlmProfileRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            BaseUrl = baseUrl,
            Model = model,
            ApiKey = apiKey,
            Enabled = request.Enabled ?? true,
            CreatedAtUtc = now,
        };
        await _profiles.InsertOneAsync(record, cancellationToken: ct);
        return CreatedAtAction(nameof(Get), new { id = record.Id }, ApiResult<AgentLlmProfileDto>.Ok(ToDto(record)));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResult<AgentLlmProfileDto>>> Update(string id, [FromBody] UpsertAgentLlmProfileRequest request, CancellationToken ct)
    {
        var existing = await Find(id).FirstOrDefaultAsync(ct);
        if (existing is null)
            return NotFound(ApiResult<AgentLlmProfileDto>.Fail("LLM 档案不存在", "LLM_PROFILE_NOT_FOUND"));

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                return BadRequest(ApiResult<AgentLlmProfileDto>.Fail("名称无效", "INVALID_INPUT"));
            existing.Name = name;
        }
        if (request.BaseUrl is not null)
            existing.BaseUrl = request.BaseUrl.Trim();
        if (request.Model is not null)
            existing.Model = request.Model.Trim();
        if (request.ClearApiKey)
            existing.ApiKey = null;
        else if (!string.IsNullOrWhiteSpace(request.ApiKey))
            existing.ApiKey = request.ApiKey.Trim();
        if (request.Enabled is not null)
            existing.Enabled = request.Enabled.Value;
        if (string.IsNullOrWhiteSpace(existing.BaseUrl) || string.IsNullOrWhiteSpace(existing.Model))
            return BadRequest(ApiResult<AgentLlmProfileDto>.Fail("BaseUrl 与模型不能为空", "INVALID_INPUT"));

        existing.UpdatedAtUtc = DateTime.UtcNow;
        await _profiles.ReplaceOneAsync(x => x.Id == existing.Id, existing, cancellationToken: ct);
        return Ok(ApiResult<AgentLlmProfileDto>.Ok(ToDto(existing)));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResult<object>>> Delete(string id, CancellationToken ct)
    {
        var existing = await Find(id).FirstOrDefaultAsync(ct);
        if (existing is null)
            return NotFound(ApiResult<object>.Fail("LLM 档案不存在", "LLM_PROFILE_NOT_FOUND"));

        var inUse = await _personas.Find(x => x.LlmProfileId == existing.Id).AnyAsync(ct);
        if (inUse)
            return Conflict(ApiResult<object>.Fail("仍有智能体绑定此档案，请先改绑或删除智能体", "LLM_PROFILE_IN_USE"));

        await _profiles.DeleteOneAsync(x => x.Id == existing.Id, ct);
        return Ok(ApiResult<object>.Ok(new { message = "删除成功" }));
    }

    [HttpPost("{id}/probe")]
    public async Task<ActionResult<ApiResult<object>>> Probe(string id, CancellationToken ct)
    {
        var profile = await Find(id).FirstOrDefaultAsync(ct);
        if (profile is null)
            return NotFound(ApiResult<object>.Fail("LLM 档案不存在", "LLM_PROFILE_NOT_FOUND"));
        var key = profile.ApiKey?.Trim() ?? "";
        if (key.Length == 0)
            return BadRequest(ApiResult<object>.Fail("未配置 LLM Key", "LLM_PROBE_FAILED"));
        try
        {
            await _llm.CompleteAsync(new(
                "你是连通性检测助手。",
                "仅回复 OK。",
                key,
                string.IsNullOrWhiteSpace(profile.BaseUrl) ? "https://api.deepseek.com" : profile.BaseUrl.Trim(),
                string.IsNullOrWhiteSpace(profile.Model) ? "deepseek-chat" : profile.Model.Trim()), ct);
            return Ok(ApiResult<object>.Ok(new { ok = true }));
        }
        catch
        {
            return StatusCode(StatusCodes.Status502BadGateway, ApiResult<object>.Fail("LLM 连通性检测失败", "LLM_PROBE_FAILED"));
        }
    }

    private IFindFluent<AgentLlmProfileRecord, AgentLlmProfileRecord> Find(string? id)
        => _profiles.Find(x => x.Id == (id ?? "").Trim());

    private static AgentLlmProfileDto ToDto(AgentLlmProfileRecord record)
    {
        var key = record.ApiKey?.Trim() ?? "";
        return new AgentLlmProfileDto
        {
            Id = record.Id,
            Name = record.Name,
            BaseUrl = record.BaseUrl,
            Model = record.Model,
            Enabled = record.Enabled,
            ApiKeyConfigured = key.Length > 0,
            ApiKeyMask = key.Length == 0 ? null : $"****{key[^Math.Min(4, key.Length)..]}",
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
        };
    }
}

public sealed class UpsertAgentLlmProfileRequest
{
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public bool ClearApiKey { get; set; }
    public bool? Enabled { get; set; }
}

public sealed class AgentLlmProfileDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public bool Enabled { get; set; }
    public bool ApiKeyConfigured { get; set; }
    public string? ApiKeyMask { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
