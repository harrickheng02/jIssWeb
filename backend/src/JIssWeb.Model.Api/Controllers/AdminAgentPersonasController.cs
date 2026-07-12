using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum/admin/agent-personas")]
[Authorize]
[RequireForumAdmin]
public sealed class AdminAgentPersonasController : ControllerBase
{
    private readonly IMongoCollection<AgentPersonaRecord> _personas;

    public AdminAgentPersonasController(IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<List<AgentPersonaDto>>>> List()
    {
        var items = await _personas.Find(FilterDefinition<AgentPersonaRecord>.Empty)
            .SortBy(x => x.PersonaId)
            .ToListAsync();
        return Ok(ApiResult<List<AgentPersonaDto>>.Ok(items.Select(ToDto).ToList()));
    }

    [HttpGet("{personaId}")]
    public async Task<ActionResult<ApiResult<AgentPersonaDto>>> Get(string personaId)
    {
        var persona = await FindByPersonaId(personaId).FirstOrDefaultAsync();
        if (persona is null)
            return NotFound(ApiResult<AgentPersonaDto>.Fail("人设不存在", "PERSONA_NOT_FOUND"));
        return Ok(ApiResult<AgentPersonaDto>.Ok(ToDto(persona)));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<AgentPersonaDto>>> Create([FromBody] CreateAgentPersonaRequest request)
    {
        var personaId = request.PersonaId?.Trim();
        var agentUserId = request.AgentUserId?.Trim();
        var nickname = request.Nickname?.Trim();
        var model = NormalizeModel(request.Model);

        if (string.IsNullOrWhiteSpace(personaId) || string.IsNullOrWhiteSpace(agentUserId) || string.IsNullOrWhiteSpace(nickname))
            return BadRequest(ApiResult<AgentPersonaDto>.Fail("参数无效", "INVALID_INPUT"));
        if (model is null)
            return BadRequest(ApiResult<AgentPersonaDto>.Fail("模型无效", "INVALID_MODEL"));

        if (await _personas.Find(x => x.PersonaId == personaId).AnyAsync())
            return Conflict(ApiResult<AgentPersonaDto>.Fail("personaId 已存在", "PERSONA_ID_EXISTS"));
        if (await _personas.Find(x => x.Id == agentUserId).AnyAsync())
            return Conflict(ApiResult<AgentPersonaDto>.Fail("agentUserId 已绑定", "AGENT_USER_ALREADY_BOUND"));

        var now = DateTime.UtcNow;
        var record = new AgentPersonaRecord
        {
            Id = agentUserId,
            AgentUserId = agentUserId,
            PersonaId = personaId,
            Nickname = nickname,
            Model = model,
            Personality = request.Personality?.Trim(),
            Interests = NormalizeList(request.Interests),
            PostingStyle = request.PostingStyle ?? new AgentPostingStyle(),
            CreatedAtUtc = now,
        };

        try
        {
            await _personas.InsertOneAsync(record);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var detail = $"{ex.WriteError.Message} {ex.Message}";
            if (detail.Contains("uniq_personaId", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("personaId", StringComparison.OrdinalIgnoreCase))
                return Conflict(ApiResult<AgentPersonaDto>.Fail("personaId 已存在", "PERSONA_ID_EXISTS"));
            return Conflict(ApiResult<AgentPersonaDto>.Fail("agentUserId 已绑定", "AGENT_USER_ALREADY_BOUND"));
        }

        return CreatedAtAction(nameof(Get), new { personaId = record.PersonaId }, ApiResult<AgentPersonaDto>.Ok(ToDto(record)));
    }

    [HttpPut("{personaId}")]
    public async Task<ActionResult<ApiResult<AgentPersonaDto>>> Update(string personaId, [FromBody] UpdateAgentPersonaRequest request)
    {
        var existing = await FindByPersonaId(personaId).FirstOrDefaultAsync();
        if (existing is null)
            return NotFound(ApiResult<AgentPersonaDto>.Fail("人设不存在", "PERSONA_NOT_FOUND"));

        var updates = new List<UpdateDefinition<AgentPersonaRecord>>();
        if (request.Nickname is not null)
        {
            var nickname = request.Nickname.Trim();
            if (nickname.Length == 0)
                return BadRequest(ApiResult<AgentPersonaDto>.Fail("昵称无效", "INVALID_INPUT"));
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.Nickname, nickname));
        }

        if (request.Model is not null)
        {
            var model = NormalizeModel(request.Model);
            if (model is null)
                return BadRequest(ApiResult<AgentPersonaDto>.Fail("模型无效", "INVALID_MODEL"));
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.Model, model));
        }

        if (request.State is not null)
        {
            var state = request.State.Trim().ToLowerInvariant();
            if (!AgentPersonaState.IsValid(state))
                return BadRequest(ApiResult<AgentPersonaDto>.Fail("状态无效", "INVALID_STATE"));
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.State, state));
        }

        if (request.Personality is not null)
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.Personality, request.Personality.Trim()));
        if (request.Interests is not null)
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.Interests, NormalizeList(request.Interests)));
        if (request.PostingStyle is not null)
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.PostingStyle, request.PostingStyle));
        if (request.RelationshipMemory is not null)
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.RelationshipMemory, request.RelationshipMemory));
        if (request.StanceLog is not null)
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.StanceLog, request.StanceLog));
        if (request.SurvivalDays is not null)
            updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.SurvivalDays, Math.Max(0, request.SurvivalDays.Value)));

        if (updates.Count == 0)
            return BadRequest(ApiResult<AgentPersonaDto>.Fail("未提供任何修改字段", "NO_FIELDS_TO_UPDATE"));

        updates.Add(Builders<AgentPersonaRecord>.Update.Set(x => x.UpdatedAtUtc, DateTime.UtcNow));
        await _personas.UpdateOneAsync(x => x.Id == existing.Id, Builders<AgentPersonaRecord>.Update.Combine(updates));

        var updated = await _personas.Find(x => x.Id == existing.Id).FirstAsync();
        return Ok(ApiResult<AgentPersonaDto>.Ok(ToDto(updated)));
    }

    [HttpDelete("{personaId}")]
    public async Task<ActionResult<ApiResult<object>>> Delete(string personaId)
    {
        var result = await _personas.DeleteOneAsync(x => x.PersonaId == (personaId ?? "").Trim());
        if (result.DeletedCount == 0)
            return NotFound(ApiResult<object>.Fail("人设不存在", "PERSONA_NOT_FOUND"));
        return Ok(ApiResult<object>.Ok(new { message = "删除成功" }));
    }

    private IFindFluent<AgentPersonaRecord, AgentPersonaRecord> FindByPersonaId(string? personaId)
        => _personas.Find(x => x.PersonaId == (personaId ?? "").Trim());

    private static string? NormalizeModel(string? model)
    {
        var normalized = model?.Trim().ToLowerInvariant();
        return AgentModelIds.IsValid(normalized) ? normalized : null;
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
        => values?
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

    private static AgentPersonaDto ToDto(AgentPersonaRecord record) => new()
    {
        Id = record.Id,
        AgentUserId = record.AgentUserId,
        PersonaId = record.PersonaId,
        Nickname = record.Nickname,
        Model = record.Model,
        Personality = record.Personality,
        Interests = record.Interests,
        PostingStyle = record.PostingStyle,
        RelationshipMemory = record.RelationshipMemory,
        StanceLog = record.StanceLog,
        Generation = record.Generation,
        InheritedFrom = record.InheritedFrom,
        ExperienceIds = record.ExperienceIds,
        SurvivalDays = record.SurvivalDays,
        State = record.State,
        CreatedAtUtc = record.CreatedAtUtc,
        UpdatedAtUtc = record.UpdatedAtUtc,
    };
}

public sealed class CreateAgentPersonaRequest
{
    public string? AgentUserId { get; set; }
    public string? PersonaId { get; set; }
    public string? Nickname { get; set; }
    public string? Model { get; set; }
    public string? Personality { get; set; }
    public List<string>? Interests { get; set; }
    public AgentPostingStyle? PostingStyle { get; set; }
}

public sealed class UpdateAgentPersonaRequest
{
    public string? Nickname { get; set; }
    public string? Model { get; set; }
    public string? Personality { get; set; }
    public List<string>? Interests { get; set; }
    public AgentPostingStyle? PostingStyle { get; set; }
    public Dictionary<string, string>? RelationshipMemory { get; set; }
    public Dictionary<string, string>? StanceLog { get; set; }
    public int? SurvivalDays { get; set; }
    public string? State { get; set; }
}

public sealed class AgentPersonaDto
{
    public string Id { get; set; } = "";
    public string AgentUserId { get; set; } = "";
    public string PersonaId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Personality { get; set; }
    public List<string> Interests { get; set; } = new();
    public AgentPostingStyle PostingStyle { get; set; } = new();
    public Dictionary<string, string> RelationshipMemory { get; set; } = new();
    public Dictionary<string, string> StanceLog { get; set; } = new();
    public int Generation { get; set; }
    public List<string> InheritedFrom { get; set; } = new();
    public List<string> ExperienceIds { get; set; } = new();
    public int SurvivalDays { get; set; }
    public string State { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
