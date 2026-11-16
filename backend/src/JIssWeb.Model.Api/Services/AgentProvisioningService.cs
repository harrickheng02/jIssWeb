using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>创建 persona 的命令（不含建号），供 HTTP 创建路径与轮换器共用。</summary>
public sealed record CreatePersonaCommand
{
    public string? AgentUserId { get; init; }
    public string? PersonaId { get; init; }
    public string? Nickname { get; init; }
    public string? Model { get; init; }
    public string? LlmProfileId { get; init; }
    public string? Prompt { get; init; }
    public string? Personality { get; init; }
    public List<string>? Interests { get; init; }
    public AgentPostingStyle? PostingStyle { get; init; }
    public List<string>? InheritedFrom { get; init; }
    /// <summary>由哪次轮换运行产生；运营手工创建时为空。</summary>
    public string? RotationRunKey { get; init; }
}

/// <summary>建号 + 创建 persona 的命令。</summary>
public sealed record ProvisionAgentCommand
{
    public string? Email { get; init; }
    public string? PersonaId { get; init; }
    public string? Nickname { get; init; }
    public string? Model { get; init; }
    public string? LlmProfileId { get; init; }
    public string? Prompt { get; init; }
    public string? Personality { get; init; }
    public List<string>? Interests { get; init; }
    public AgentPostingStyle? PostingStyle { get; init; }
    public List<string>? InheritedFrom { get; init; }
    public string? RotationRunKey { get; init; }
}

/// <summary>编排结果：不含 <c>ActionResult</c>，便于后台服务复用。</summary>
public sealed record AgentProvisionOutcome
{
    public bool Success { get; init; }
    public int HttpStatus { get; init; } = 200;
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public AgentPersonaRecord? Persona { get; init; }

    public static AgentProvisionOutcome Ok(AgentPersonaRecord persona)
        => new() { Success = true, HttpStatus = 201, Persona = persona };

    public static AgentProvisionOutcome Fail(int status, string code, string message)
        => new() { Success = false, HttpStatus = status, ErrorCode = code, ErrorMessage = message };
}

/// <summary>
/// 智能体供给服务（V0.3）：把「建号 → 写 persona → 初始化调度状态 → 应用经验继承 → 失败补偿」
/// 从 <c>AdminAgentPersonasController</c> 下沉，供 HTTP 路径与账号轮换器共用。
/// 错误码与 HTTP 状态码保持与重构前逐字一致。
/// </summary>
public interface IAgentProvisioningService
{
    /// <summary>校验并写入 persona + scheduler state（不含建号）。</summary>
    Task<AgentProvisionOutcome> CreatePersonaAsync(CreatePersonaCommand command, CancellationToken ct = default);

    /// <summary>建号 → <see cref="CreatePersonaAsync"/> → 失败时停用新账号。</summary>
    Task<AgentProvisionOutcome> ProvisionAsync(ProvisionAgentCommand command, CancellationToken ct = default);
}

public sealed class AgentProvisioningService : IAgentProvisioningService
{
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<AgentSchedulerStateRecord> _schedulerStates;
    private readonly IMongoCollection<AgentLlmProfileRecord> _profiles;
    private readonly AgentSchedulerOptions _schedulerOptions;
    private readonly IAgentUserInternalClient _agentUsers;
    private readonly IExperienceInheritanceService _inheritance;
    private readonly ILogger<AgentProvisioningService> _logger;

    public AgentProvisioningService(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions,
        IOptions<AgentSchedulerOptions> schedulerOptions,
        IAgentUserInternalClient agentUsers, IExperienceInheritanceService inheritance,
        ILogger<AgentProvisioningService> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _schedulerStates = db.GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName);
        _profiles = db.GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName);
        _schedulerOptions = schedulerOptions.Value;
        _agentUsers = agentUsers;
        _inheritance = inheritance;
        _logger = logger;
    }

    public async Task<AgentProvisionOutcome> ProvisionAsync(ProvisionAgentCommand command, CancellationToken ct = default)
    {
        var nickname = command.Nickname?.Trim();
        if (string.IsNullOrWhiteSpace(nickname))
            return AgentProvisionOutcome.Fail(400, "INVALID_INPUT", "参数无效");

        var personaId = string.IsNullOrWhiteSpace(command.PersonaId)
            ? $"persona-{Guid.NewGuid():N}"
            : command.PersonaId.Trim();
        var email = command.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            email = $"{personaId}@agents.local";

        var agent = await _agentUsers.CreateAsync(email, nickname, ct);
        if (agent is null)
            return AgentProvisionOutcome.Fail(502, "AGENT_PROVISION_FAILED", "智能体账号创建失败");

        var outcome = await CreatePersonaAsync(new CreatePersonaCommand
        {
            AgentUserId = agent.AgentUserId,
            PersonaId = personaId,
            Nickname = nickname,
            Model = command.Model,
            LlmProfileId = command.LlmProfileId,
            Prompt = command.Prompt,
            Personality = command.Personality,
            Interests = command.Interests,
            PostingStyle = command.PostingStyle,
            InheritedFrom = command.InheritedFrom,
            RotationRunKey = command.RotationRunKey,
        }, ct);

        if (!outcome.Success)
        {
            var disabled = await _agentUsers.DisableAsync(agent.AgentUserId, ct);
            if (!disabled)
                _logger.LogWarning("Rotation/provision compensation failed: could not disable orphan agent account {AgentUserId}.", agent.AgentUserId);
        }

        return outcome;
    }

    public async Task<AgentProvisionOutcome> CreatePersonaAsync(CreatePersonaCommand command, CancellationToken ct = default)
    {
        var personaId = string.IsNullOrWhiteSpace(command.PersonaId)
            ? $"persona-{Guid.NewGuid():N}"
            : command.PersonaId.Trim();
        var agentUserId = command.AgentUserId?.Trim();
        var nickname = command.Nickname?.Trim();
        var llmProfileId = command.LlmProfileId?.Trim() ?? "";
        var model = string.IsNullOrWhiteSpace(command.Model)
            ? AgentModelIds.Deepseek
            : NormalizeModel(command.Model);

        if (string.IsNullOrWhiteSpace(agentUserId) || string.IsNullOrWhiteSpace(nickname))
            return AgentProvisionOutcome.Fail(400, "INVALID_INPUT", "参数无效");
        if (model is null)
            return AgentProvisionOutcome.Fail(400, "INVALID_MODEL", "模型无效");
        if (llmProfileId.Length == 0)
            return AgentProvisionOutcome.Fail(400, "LLM_PROFILE_REQUIRED", "请选择 LLM 档案");
        if (!await _profiles.Find(x => x.Id == llmProfileId).AnyAsync(ct))
            return AgentProvisionOutcome.Fail(400, "LLM_PROFILE_NOT_FOUND", "LLM 档案不存在");

        if (await _personas.Find(x => x.PersonaId == personaId).AnyAsync(ct))
            return AgentProvisionOutcome.Fail(409, "PERSONA_ID_EXISTS", "personaId 已存在");
        if (await _personas.Find(x => x.Id == agentUserId).AnyAsync(ct))
            return AgentProvisionOutcome.Fail(409, "AGENT_USER_ALREADY_BOUND", "agentUserId 已绑定");

        var now = DateTime.UtcNow;
        var record = new AgentPersonaRecord
        {
            Id = agentUserId,
            AgentUserId = agentUserId,
            PersonaId = personaId,
            Nickname = nickname,
            Model = model,
            LlmProfileId = llmProfileId,
            Prompt = command.Prompt?.Trim(),
            Personality = command.Personality,
            Interests = command.Interests ?? new List<string>(),
            PostingStyle = command.PostingStyle ?? new AgentPostingStyle(),
            RotationRunKey = command.RotationRunKey,
            CreatedAtUtc = now,
        };

        // #38 继承注入 + 世代赋值（失败不影响创建）。
        try
        {
            await _inheritance.ApplyAsync(record, command.InheritedFrom, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inheritance apply failed for persona {PersonaId}.", personaId);
        }

        try
        {
            await _personas.InsertOneAsync(record, cancellationToken: ct);
            await _schedulerStates.InsertOneAsync(AgentSchedulerQuota.CreateDefault(record, _schedulerOptions, now), cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var detail = $"{ex.WriteError.Message} {ex.Message}";
            if (detail.Contains("uniq_personaId", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("personaId", StringComparison.OrdinalIgnoreCase))
                return AgentProvisionOutcome.Fail(409, "PERSONA_ID_EXISTS", "personaId 已存在");
            return AgentProvisionOutcome.Fail(409, "AGENT_USER_ALREADY_BOUND", "agentUserId 已绑定");
        }

        return AgentProvisionOutcome.Ok(record);
    }

    internal static string? NormalizeModel(string? model)
    {
        var normalized = model?.Trim().ToLowerInvariant();
        return AgentModelIds.IsValid(normalized) ? normalized : null;
    }
}
