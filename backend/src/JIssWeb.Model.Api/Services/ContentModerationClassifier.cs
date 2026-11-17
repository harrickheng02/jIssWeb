using System.Text;
using System.Text.Json;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>内容判定结果。<see cref="ForumModerationVerdicts.Ok"/> 表示放行。</summary>
public sealed record ModerationVerdict(string Verdict, double Confidence, string? Reason, string? LlmProfileId = null)
{
    public static readonly ModerationVerdict Ok = new(ForumModerationVerdicts.Ok, 0d, null);

    public bool IsViolation => Verdict != ForumModerationVerdicts.Ok;
}

public interface IContentModerator
{
    /// <summary>对单条内容做判定；任何失败形态都返回 <see cref="ModerationVerdict.Ok"/>。</summary>
    Task<ModerationVerdict> ClassifyAsync(string? title, string body, CancellationToken ct = default);
}

/// <summary>
/// AI 版主阶段一：LLM 内容分类器。
/// 铁律：prompt 只含标题与正文，绝不带屏蔽词表或屏蔽命中结果。
/// temperature=0 + JSON 契约，解析失败一律回落 ok（fail-open）。
/// </summary>
public sealed class LlmContentModerator : IContentModerator
{
    private const string SystemPrompt =
        "你是论坛内容审核分类器。判断给定内容是否违反社区规则，仅输出一行 JSON：" +
        "{\"verdict\":\"ok|spam|sexual|political|other\",\"confidence\":0.0-1.0,\"reason\":\"不超过40字的中文理由\"}。" +
        "spam 指广告推广、引流、重复刷屏；sexual 指色情或性暗示；political 指政治敏感；other 指其他明显违规；" +
        "正常讨论、吐槽、情绪表达一律 ok。不要输出任何解释或代码块标记。";

    private readonly IMongoCollection<AgentLlmProfileRecord> _profiles;
    private readonly IAgentLlmConfigResolver _config;
    private readonly ILlmChatClient _llm;
    private readonly ForumContentModerationOptions _options;
    private readonly ILogger<LlmContentModerator> _logger;

    public LlmContentModerator(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions,
        IAgentLlmConfigResolver config, ILlmChatClient llm,
        IOptions<ForumContentModerationOptions> options,
        ILogger<LlmContentModerator> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _profiles = db.GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName);
        _config = config;
        _llm = llm;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ModerationVerdict> ClassifyAsync(string? title, string body, CancellationToken ct = default)
    {
        try
        {
            var profile = await ResolveProfileAsync(ct);
            if (profile is null || string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                _logger.LogDebug("Content moderation skipped: no usable LLM profile.");
                return ModerationVerdict.Ok;
            }

            var user = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(title)) user.Append("标题：").Append(Truncate(title, 200)).Append('\n');
            user.Append("正文：").Append(Truncate(body, 4000));

            var raw = await _llm.CompleteAsync(
                new LlmChatRequest(SystemPrompt, user.ToString(), profile.ApiKey, profile.BaseUrl, profile.Model,
                    Temperature: 0, MaxTokens: 200),
                ct);
            var verdict = Parse(raw);
            return verdict with { LlmProfileId = profile.Id };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content moderation classification failed; fail-open.");
            return ModerationVerdict.Ok;
        }
    }

    private async Task<ResolvedLlmProfile?> ResolveProfileAsync(CancellationToken ct)
    {
        var configured = await _config.ResolveProfileAsync(_options.LlmProfileId, ct);
        if (configured is not null) return configured;

        // 未配置专属档案时回退到第一个启用的档案，避免运营者必须手工填 Id 才能用。
        var fallback = await _profiles.Find(x => x.Enabled).FirstOrDefaultAsync(ct);
        if (fallback is null) return null;
        return new ResolvedLlmProfile(
            fallback.Id,
            fallback.Name,
            fallback.ApiKey?.Trim() ?? "",
            string.IsNullOrWhiteSpace(fallback.BaseUrl) ? "https://api.deepseek.com" : fallback.BaseUrl.Trim(),
            string.IsNullOrWhiteSpace(fallback.Model) ? "deepseek-chat" : fallback.Model.Trim(),
            fallback.Enabled);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    public static ModerationVerdict Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ModerationVerdict.Ok;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return ModerationVerdict.Ok;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ModerationVerdict.Ok;

            var verdict = root.TryGetProperty("verdict", out var v) ? v.GetString()?.Trim().ToLowerInvariant() ?? "" : "";
            if (!ForumModerationVerdicts.Known.Contains(verdict)) return ModerationVerdict.Ok;
            if (verdict == ForumModerationVerdicts.Ok) return ModerationVerdict.Ok;

            if (!root.TryGetProperty("confidence", out var c) || c.ValueKind != JsonValueKind.Number
                || !c.TryGetDouble(out var confidence))
                return ModerationVerdict.Ok;

            confidence = Math.Clamp(confidence, 0d, 1d);
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString()?.Trim() : null;
            if (!string.IsNullOrWhiteSpace(reason) && reason.Length > 200) reason = reason[..200];
            return new ModerationVerdict(verdict, confidence, string.IsNullOrWhiteSpace(reason) ? null : reason);
        }
        catch (Exception)
        {
            return ModerationVerdict.Ok;
        }
    }
}
