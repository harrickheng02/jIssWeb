using System.Collections.Generic;
using System.Text.Json;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>注入提示词的人设记忆上下文（仅含已沉淀的键值，不引入外部依赖）。</summary>
public sealed record AgentMemoryContext(
    IReadOnlyDictionary<string, string> RelationshipMemory,
    IReadOnlyDictionary<string, string> StanceLog,
    IReadOnlyList<string>? RecentDialogues);

public sealed record ExtractedStance(string Topic, string Stance);

/// <summary>一次记忆抽取结果；PatternType 非空表示识别到可沉淀模式（触发经验写入）。</summary>
public sealed record MemoryExtraction(
    string UserSentiment,
    IReadOnlyList<ExtractedStance> TopicStances,
    string? PatternType = null,
    string? Description = null);

/// <summary>记忆抽取抽象：给定发布内容与话题提示，抽取情感倾向与话题立场。</summary>
public interface IAgentMemoryExtractor
{
    Task<MemoryExtraction?> ExtractAsync(ResolvedLlmProfile profile, string body, string? topicHint, CancellationToken ct);
}

/// <summary>基于 LLM 档案的轻量记忆抽取。抽取失败返回 null（由上层降级为确定性信号）。</summary>
public sealed class LlmMemoryExtractor : IAgentMemoryExtractor
{
    private readonly ILlmChatClient _llm;
    private readonly ILogger<LlmMemoryExtractor> _logger;
    public LlmMemoryExtractor(ILlmChatClient llm, ILogger<LlmMemoryExtractor> logger)
    {
        _llm = llm; _logger = logger;
    }

    public async Task<MemoryExtraction?> ExtractAsync(ResolvedLlmProfile profile, string body, string? topicHint, CancellationToken ct)
    {
        const string sys = "你是记忆抽取器。从一段论坛发言中抽取：(1) 作者对对话对象的情感倾向（简短中文短语，如「友好、乐于讨论」）；" +
                           "(2) 作者对相关话题的立场（topic:stance 列表）。若无可沉淀模式，patternType 与 description 留空。" +
                           "仅输出一行 JSON，格式：{\"userSentiment\":\"...\",\"topicStances\":[{\"topic\":\"...\",\"stance\":\"...\"}],\"patternType\":\"\",\"description\":\"\"}。不要输出任何解释。";
        var user = $"发言内容：\n{body}\n话题提示：{topicHint ?? "（无）"}";
        string raw;
        try
        {
            raw = await _llm.CompleteAsync(new(sys, user, profile.ApiKey, profile.BaseUrl, profile.Model), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory extraction LLM call failed; degrading to deterministic signal.");
            return null;
        }
        return Parse(raw);
    }

    private static MemoryExtraction? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;
            var sentiment = root.TryGetProperty("userSentiment", out var s) ? s.GetString() ?? "" : "";
            var stances = new List<ExtractedStance>();
            if (root.TryGetProperty("topicStances", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.TryGetProperty("topic", out var t) && item.TryGetProperty("stance", out var st))
                    {
                        var topic = t.GetString()?.Trim();
                        var stance = st.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(topic) && !string.IsNullOrWhiteSpace(stance))
                            stances.Add(new(topic, stance));
                    }
                }
            }
            var pattern = root.TryGetProperty("patternType", out var p) ? p.GetString()?.Trim() : null;
            var desc = root.TryGetProperty("description", out var d) ? d.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(sentiment) && stances.Count == 0 && string.IsNullOrWhiteSpace(pattern))
                return null;
            return new MemoryExtraction(sentiment ?? "", stances, string.IsNullOrWhiteSpace(pattern) ? null : pattern, desc);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// 记忆回写服务：发帖/回复成功后原子更新 persona 的记忆字段，并在识别到可沉淀模式时内部写 agent_experiences。
/// 当 MemoryEnabled 为 false 时完全不生效（回归 #31）。
/// </summary>
public sealed class AgentMemoryUpdater
{
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<AgentExperienceRecord> _experiences;
    private readonly IAgentMemoryExtractor _extractor;
    private readonly AgentSchedulerOptions _options;
    private readonly ILogger<AgentMemoryUpdater> _logger;

    public AgentMemoryUpdater(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions, IAgentMemoryExtractor extractor,
        IOptions<AgentSchedulerOptions> options, ILogger<AgentMemoryUpdater> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _experiences = db.GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName);
        _extractor = extractor; _options = options.Value; _logger = logger;
    }

    public async Task UpdateAsync(
        AgentPersonaRecord persona, AgentScheduledAction? action, string? targetUserSub, string body, string board,
        ResolvedLlmProfile profile, CancellationToken ct)
    {
        if (!_options.MemoryEnabled) return;
        if (action is null) return;

        MemoryExtraction? ext = null;
        if (string.Equals(_options.MemoryExtractionMode, "llm", StringComparison.OrdinalIgnoreCase))
        {
            try { ext = await _extractor.ExtractAsync(profile, body, board, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Memory extractor threw; degrading to deterministic signal."); }
        }

        var update = Builders<AgentPersonaRecord>.Update;
        var sets = new List<UpdateDefinition<AgentPersonaRecord>>();

        // 回复 → 关系记忆（目标用户）；发帖 → 不更新关系记忆（无单一目标）。
        if (action == AgentScheduledAction.Reply && !string.IsNullOrWhiteSpace(targetUserSub))
        {
            var sentiment = !string.IsNullOrWhiteSpace(ext?.UserSentiment) ? ext!.UserSentiment : "曾互动";
            sets.Add(update.Set($"RelationshipMemory.{targetUserSub}", sentiment));
        }

        // 立场记忆：优先用抽取的话题立场，否则按板块记「曾参与讨论」。
        if (ext is { TopicStances.Count: > 0 })
        {
            foreach (var ts in ext.TopicStances)
                sets.Add(update.Set($"StanceLog.{ts.Topic}", ts.Stance));
        }
        else
        {
            sets.Add(update.Set($"StanceLog.{board}", "曾参与讨论"));
        }

        sets.Add(update.Set(x => x.UpdatedAtUtc, DateTime.UtcNow));

        try
        {
            await _personas.UpdateOneAsync(
                x => x.Id == persona.Id,
                update.Combine(sets),
                new UpdateOptions { IsUpsert = true },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory write-back failed for {PersonaId}; publish outcome unaffected.", persona.PersonaId);
            return;
        }

        // 经验写入：仅在识别到可沉淀模式时。
        if (ext is { PatternType: { Length: > 0 } pattern })
        {
            try
            {
                var expId = Guid.NewGuid().ToString("N");
                var record = new AgentExperienceRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ExperienceId = expId,
                    SourcePersonaId = persona.AgentUserId,
                    PatternType = pattern,
                    Description = ext.Description ?? "",
                    ExampleTexts = new List<string> { body.Length > 500 ? body[..500] : body },
                    EffectivenessScore = 1.0,
                    Weight = 1.0,
                    InheritedBy = new List<string>(),
                    CreatedAtUtc = DateTime.UtcNow,
                };
                await _experiences.InsertOneAsync(record, null, ct);
                await _personas.UpdateOneAsync(
                    x => x.Id == persona.Id && !x.ExperienceIds.Contains(expId),
                    update.AddToSet(x => x.ExperienceIds, expId),
                    cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
            {
                _logger.LogWarning("ExperienceId duplicate-key conflict ignored (idempotent).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Experience write failed for {PersonaId}.", persona.PersonaId);
            }
        }
    }
}
