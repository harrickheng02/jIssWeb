using System.Security.Cryptography;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>参赛池实时水位。</summary>
public sealed record AgentRotationStatus(
    bool Enabled,
    long ActiveCount,
    int MinActiveAgents,
    int MaxActiveAgents,
    int MaxPerRun,
    long Gap);

/// <summary>
/// 账号轮换器（V0.3）：被淘汰账号后自动补建新智能体账号以维持参赛池。
/// 池子口径为 <c>agent_personas.state == "active"</c> 的实时计数，不引入 roster 快照。
/// </summary>
public interface IAgentRotationService
{
    /// <summary>
    /// 按实时缺口补齐参赛池。返回本次运行记录；若 runKey 已存在（重复触发）返回 <c>null</c>。
    /// 开关关闭时直接返回 <c>null</c> 且不写任何集合。
    /// </summary>
    Task<AgentRotationRunRecord?> EnsurePoolAsync(
        string trigger, string runKey, string? triggeredBySub = null, CancellationToken ct = default);

    /// <summary>
    /// 周结算完成（<c>reveal_jobs.state=completed</c> 已置位）后的补齐钩子。
    /// 异常在此内聚吞掉并记录日志，保证结算结果、揭露状态与奖章发放不受影响。
    /// </summary>
    Task<AgentRotationRunRecord?> EnsureAfterRevealAsync(string weekKey, CancellationToken ct = default);

    Task<AgentRotationStatus> GetStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AgentRotationRunRecord>> ListRunsAsync(int limit, CancellationToken ct = default);
}

public sealed class AgentRotationService : IAgentRotationService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int NicknameCodeLength = 4;
    private const int MaxFailureNotes = 5;

    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<AgentLlmProfileRecord> _profiles;
    private readonly IMongoCollection<AgentRotationRunRecord> _runs;
    private readonly AgentRotationOptions _options;
    private readonly IAgentProvisioningService _provisioning;
    private readonly ILogger<AgentRotationService> _logger;

    public AgentRotationService(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions,
        IOptions<AgentRotationOptions> options,
        IAgentProvisioningService provisioning, ILogger<AgentRotationService> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _profiles = db.GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName);
        _runs = db.GetCollection<AgentRotationRunRecord>(AgentMongoSetup.RotationRunsCollectionName);
        _options = options.Value;
        _provisioning = provisioning;
        _logger = logger;
    }

    public async Task<AgentRotationStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var active = await CountActiveAsync(ct);
        return new AgentRotationStatus(
            _options.Enabled,
            active,
            _options.MinActiveAgents,
            _options.MaxActiveAgents,
            _options.MaxPerRun,
            _options.MinActiveAgents - active);
    }

    public async Task<IReadOnlyList<AgentRotationRunRecord>> ListRunsAsync(int limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, 100);
        return await _runs.Find(FilterDefinition<AgentRotationRunRecord>.Empty)
            .SortByDescending(x => x.StartedAtUtc)
            .Limit(take)
            .ToListAsync(ct);
    }

    public async Task<AgentRotationRunRecord?> EnsurePoolAsync(
        string trigger, string runKey, string? triggeredBySub = null, CancellationToken ct = default)
    {
        if (!_options.Enabled) return null;

        var run = new AgentRotationRunRecord
        {
            RunKey = runKey,
            Trigger = trigger,
            StartedAtUtc = DateTime.UtcNow,
            TriggeredBySub = string.IsNullOrWhiteSpace(triggeredBySub) ? null : triggeredBySub.Trim(),
        };

        // 幂等认领：runKey 唯一；重复触发 11000 静默跳过。
        try
        {
            await _runs.InsertOneAsync(run, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return null;
        }

        var activeBefore = (int)await CountActiveAsync(ct);
        var gap = _options.MinActiveAgents - activeBefore;
        run.GapBefore = gap;

        var take = Math.Min(
            Math.Max(gap, 0),
            Math.Min(Math.Max(_options.MaxPerRun, 0), Math.Max(_options.MaxActiveAgents - activeBefore, 0)));

        var created = 0;
        var failed = 0;
        var failures = new List<string>();

        if (take > 0)
        {
            var sources = await LoadLineageSourcesAsync(take, ct);
            for (var i = 0; i < take; i++)
            {
                var source = i < sources.Count ? sources[i] : null;
                try
                {
                    var outcome = await ProvisionReplacementAsync(source, runKey, ct);
                    if (outcome.Success) created++;
                    else
                    {
                        failed++;
                        Note(failures, $"{outcome.ErrorCode}: {outcome.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Note(failures, ex.GetType().Name + ": " + ex.Message);
                    _logger.LogWarning(ex, "Rotation replacement failed for run {RunKey} (source {Source}).",
                        runKey, source?.PersonaId ?? "<none>");
                }
            }
        }

        run.CreatedCount = created;
        run.FailedCount = failed;
        run.ActiveCountAfter = (int)await CountActiveAsync(ct);
        run.Failures = failures;
        run.CompletedAtUtc = DateTime.UtcNow;

        await _runs.UpdateOneAsync(
            x => x.Id == run.Id,
            Builders<AgentRotationRunRecord>.Update
                .Set(x => x.GapBefore, run.GapBefore)
                .Set(x => x.CreatedCount, run.CreatedCount)
                .Set(x => x.FailedCount, run.FailedCount)
                .Set(x => x.ActiveCountAfter, run.ActiveCountAfter)
                .Set(x => x.Failures, run.Failures)
                .Set(x => x.CompletedAtUtc, run.CompletedAtUtc),
            cancellationToken: ct);

        if (created > 0 || failed > 0)
        {
            _logger.LogInformation(
                "Agent rotation run {RunKey} ({Trigger}): gapBefore={Gap}, created={Created}, failed={Failed}, activeAfter={After}.",
                runKey, trigger, run.GapBefore, created, failed, run.ActiveCountAfter);
        }

        return run;
    }

    public async Task<AgentRotationRunRecord?> EnsureAfterRevealAsync(string weekKey, CancellationToken ct = default)
    {
        try
        {
            return await EnsurePoolAsync(AgentRotationTriggers.Reveal, $"{AgentRotationTriggers.Reveal}:{weekKey}", null, ct);
        }
        catch (Exception ex)
        {
            // 结算已完成（completed 已置位），补齐失败 SHALL NOT 影响结算、揭露与奖章结果。
            _logger.LogWarning(ex, "Agent rotation after reveal week {WeekKey} failed; settlement unaffected.", weekKey);
            return null;
        }
    }

    private async Task<AgentProvisionOutcome> ProvisionReplacementAsync(
        AgentPersonaRecord? source, string runKey, CancellationToken ct)
    {
        var nickname = await GenerateNicknameAsync(ct);
        if (nickname is null)
            return AgentProvisionOutcome.Fail(409, "NICKNAME_EXHAUSTED", "无法生成唯一昵称");

        var profileId = await ResolveProfileIdAsync(source, ct);
        if (profileId is null)
            return AgentProvisionOutcome.Fail(400, "LLM_PROFILE_REQUIRED", "无可用 LLM 档案");

        return await _provisioning.ProvisionAsync(new ProvisionAgentCommand
        {
            PersonaId = $"persona-{Guid.NewGuid():N}",
            Nickname = nickname,
            Model = string.IsNullOrWhiteSpace(source?.Model) ? AgentModelIds.Deepseek : source!.Model,
            LlmProfileId = profileId,
            Prompt = source?.Prompt,
            Personality = source?.Personality,
            // 旧文档可能缺 Interests 字段（反序列化为 null），显式兜底避免 NRE。
            Interests = source?.Interests is { Count: > 0 } interests ? new List<string>(interests) : null,
            PostingStyle = source?.PostingStyle,
            InheritedFrom = source is null ? null : new List<string> { source.PersonaId },
            RotationRunKey = runKey,
        }, ct);
    }

    private async Task<List<AgentPersonaRecord>> LoadLineageSourcesAsync(int take, CancellationToken ct)
    {
        if (take <= 0) return new List<AgentPersonaRecord>();
        return await _personas
            .Find(Builders<AgentPersonaRecord>.Filter.Eq(x => x.State, AgentPersonaState.Eliminated))
            .SortByDescending(x => x.UpdatedAtUtc)
            .Limit(take)
            .ToListAsync(ct);
    }

    private async Task<string?> ResolveProfileIdAsync(AgentPersonaRecord? source, CancellationToken ct)
    {
        var sourceId = source?.LlmProfileId?.Trim();
        if (!string.IsNullOrWhiteSpace(sourceId)
            && await _profiles.Find(x => x.Id == sourceId).AnyAsync(ct))
            return sourceId;

        var fallback = await _profiles
            .Find(Builders<AgentLlmProfileRecord>.Filter.Eq(x => x.Enabled, true))
            .SortBy(x => x.CreatedAtUtc)
            .Limit(1)
            .FirstOrDefaultAsync(ct);
        return fallback?.Id;
    }

    private async Task<string?> GenerateNicknameAsync(CancellationToken ct)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.ReplacementNicknamePrefix)
            ? "观察者"
            : _options.ReplacementNicknamePrefix.Trim();
        var attempts = Math.Max(1, _options.NicknameRetryLimit);

        for (var i = 0; i < attempts; i++)
        {
            var code = new char[NicknameCodeLength];
            for (var j = 0; j < NicknameCodeLength; j++)
                code[j] = Base32Alphabet[RandomNumberGenerator.GetInt32(Base32Alphabet.Length)];

            var nickname = prefix + new string(code);
            if (!await _personas.Find(x => x.Nickname == nickname).AnyAsync(ct))
                return nickname;
        }

        return null;
    }

    private Task<long> CountActiveAsync(CancellationToken ct)
        => _personas.CountDocumentsAsync(
            Builders<AgentPersonaRecord>.Filter.Eq(x => x.State, AgentPersonaState.Active),
            cancellationToken: ct);

    private static void Note(List<string> failures, string note)
    {
        if (failures.Count < MaxFailureNotes) failures.Add(note);
    }
}
