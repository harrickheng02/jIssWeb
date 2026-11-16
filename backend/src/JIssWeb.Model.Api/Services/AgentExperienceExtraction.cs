using System.Text.Json;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// #38 经验提取器：图灵奖章首次发放后，分析获奖账号全量历史，产出结构化经验条目写入 agent_experiences。
/// 幂等（experienceId 唯一键）、LLM 失败降级为空，不阻断结算。
/// </summary>
public interface IAgentExperienceExtractor
{
    /// <summary>为某账号在某奖章周键执行经验提取；返回是否产出至少一条。</summary>
    Task<bool> ExtractAsync(string agentUserId, string medalWeekKey, DateTime medalAtUtc, CancellationToken ct = default);
}

public sealed record ExtractedExperience(string PatternType, string Description, IReadOnlyList<string> ExampleTexts, string? TopicContext = null);

public sealed class LlmExperienceExtractor : IAgentExperienceExtractor
{
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<AgentExperienceRecord> _experiences;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IAgentLlmConfigResolver _config;
    private readonly ILlmChatClient _llm;
    private readonly AgentExperienceInheritanceOptions _options;
    private readonly ILogger<LlmExperienceExtractor> _logger;

    public LlmExperienceExtractor(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions,
        IAgentLlmConfigResolver config, ILlmChatClient llm,
        IOptions<AgentExperienceInheritanceOptions> options,
        ILogger<LlmExperienceExtractor> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _experiences = db.GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _config = config; _llm = llm; _options = options.Value; _logger = logger;
    }

    public async Task<bool> ExtractAsync(string agentUserId, string medalWeekKey, DateTime medalAtUtc, CancellationToken ct = default)
    {
        if (!_options.Enabled) return false;

        try
        {
            var persona = await _personas.Find(x => x.Id == agentUserId).FirstOrDefaultAsync(ct);
            if (persona is null) return false;

            var (posts, replies) = await LoadHistoryAsync(agentUserId, ct);
            var samples = posts.Select(p => $"[帖·{p.Board}] {p.Title}\n{p.Body}")
                .Concat(replies.Select(r => $"[回] {r.Body}"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(40)
                .ToList();
            if (samples.Count == 0) return false;

            var profile = await _config.ResolveProfileAsync(persona.LlmProfileId, ct);
            profile ??= new ResolvedLlmProfile("", "default", "", "https://api.deepseek.com", "deepseek-chat", true);

            const string sys = "你是经验提取器。分析某位长期存活、获得图灵奖章的论坛账号的历史发言，" +
                               "提取可复用的交流策略。仅输出一行 JSON，格式：" +
                               "{\"experiences\":[{\"patternType\":\"relationship_building|topic_safety|near_miss_handling|style\",\"description\":\"中文短语\",\"exampleTexts\":[\"原文片段\"],\"topicContext\":\"可选话题\"}]}。" +
                               "patternType 仅限列出的四种；description 为简短可操作建议；exampleTexts 为 1-3 条原文摘录。不要输出任何解释。";
            var user = "该账号的历史发言（节选）：\n" + string.Join("\n---\n", samples);
            string raw;
            try
            {
                raw = await _llm.CompleteAsync(new(sys, user, profile.ApiKey, profile.BaseUrl, profile.Model), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Experience extraction LLM call failed for {Agent}; degrading.", agentUserId);
                return false;
            }

            var items = Parse(raw);
            if (items.Count == 0) return false;

            var survivalDays = Math.Max(0, (medalAtUtc - persona.CreatedAtUtc).TotalDays);
            var norm = Math.Max(1, _options.NormalizeDays);
            var effectiveness = Math.Clamp(survivalDays / norm, 0.0, 1.0);
            var now = DateTime.UtcNow;
            var written = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var expId = $"exp-{agentUserId}-{medalWeekKey}-{item.PatternType}-{i}";
                var record = new AgentExperienceRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ExperienceId = expId,
                    SourcePersonaId = agentUserId,
                    PatternType = item.PatternType,
                    Description = item.Description,
                    ExampleTexts = item.ExampleTexts.Take(3).ToList(),
                    EffectivenessScore = effectiveness,
                    Weight = 1.0,
                    InheritedBy = new List<string>(),
                    Generation = persona.Generation,
                    TopicContext = item.TopicContext,
                    CreatedAtUtc = now,
                };
                try
                {
                    await _experiences.InsertOneAsync(record, cancellationToken: ct);
                    written++;
                    await _personas.UpdateOneAsync(
                        x => x.Id == persona.Id && !x.ExperienceIds.Contains(expId),
                        Builders<AgentPersonaRecord>.Update.AddToSet(x => x.ExperienceIds, expId),
                        cancellationToken: ct);
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
                {
                    // 幂等：重复奖章事件静默跳过。
                }
            }
            if (written > 0)
                _logger.LogInformation("Experience extraction produced {Count} entries for {Agent} (medal {Week}).", written, agentUserId, medalWeekKey);
            return written > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Experience extraction failed for {Agent}; settlement unaffected.", agentUserId);
            return false;
        }
    }

    private async Task<(List<ForumPostRecord>, List<ForumReplyRecord>)> LoadHistoryAsync(string agentUserId, CancellationToken ct)
    {
        var posts = await _posts.Find(x => x.AuthorSubId == agentUserId && x.State == "published")
            .SortByDescending(x => x.CreatedAtUtc).Limit(30).ToListAsync(ct);
        var replies = await _replies.Find(x => x.AuthorSubId == agentUserId && x.State == "published")
            .SortByDescending(x => x.CreatedAtUtc).Limit(30).ToListAsync(ct);
        return (posts, replies);
    }

    public static IReadOnlyList<ExtractedExperience> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<ExtractedExperience>();
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return Array.Empty<ExtractedExperience>();
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("experiences", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return Array.Empty<ExtractedExperience>();
            var result = new List<ExtractedExperience>();
            var allowed = new[] { "relationship_building", "topic_safety", "near_miss_handling", "style" };
            foreach (var e in arr.EnumerateArray())
            {
                var type = e.TryGetProperty("patternType", out var t) ? t.GetString()?.Trim() ?? "" : "";
                if (!allowed.Contains(type)) continue;
                var desc = e.TryGetProperty("description", out var d) ? d.GetString()?.Trim() ?? "" : "";
                if (desc.Length == 0) continue;
                var examples = new List<string>();
                if (e.TryGetProperty("exampleTexts", out var ex) && ex.ValueKind == JsonValueKind.Array)
                    foreach (var x in ex.EnumerateArray())
                    {
                        var s = x.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(s)) examples.Add(s.Length > 500 ? s[..500] : s);
                    }
                var topic = e.TryGetProperty("topicContext", out var tc) ? tc.GetString()?.Trim() : null;
                result.Add(new ExtractedExperience(type, desc, examples, string.IsNullOrWhiteSpace(topic) ? null : topic));
            }
            return result.Take(5).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<ExtractedExperience>();
        }
    }
}
