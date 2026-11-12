using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public sealed record LlmChatRequest(string SystemPrompt, string UserPrompt, string ApiKey, string BaseUrl, string Model);
public interface ILlmChatClient
{
    Task<string> CompleteAsync(LlmChatRequest request, CancellationToken ct = default);
}

public sealed class DeepSeekChatClient(HttpClient http) : ILlmChatClient
{
    public async Task<string> CompleteAsync(LlmChatRequest request, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{request.BaseUrl.TrimEnd('/')}/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        message.Content = JsonContent.Create(new
        {
            model = request.Model,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
            temperature = 0.8,
        });
        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var choices = document.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            throw new InvalidOperationException("LLM returned no choices.");
        return choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim()
            ?? throw new InvalidOperationException("LLM returned empty content.");
    }
}

public sealed record EffectiveAgentLlmConfig(string ApiKey, string BaseUrl, string Model, bool SchedulerEnabled);
public sealed record ResolvedLlmProfile(string Id, string Name, string ApiKey, string BaseUrl, string Model, bool Enabled);

public interface IAgentLlmConfigResolver
{
    Task<bool> IsSchedulerEnabledAsync(CancellationToken ct = default);
    Task<AgentRuntimeConfigRecord?> GetRuntimeAsync(CancellationToken ct = default);
    Task<ResolvedLlmProfile?> ResolveProfileAsync(string? profileId, CancellationToken ct = default);
    /// <summary>兼容旧调用：仅返回全局调度开关；ApiKey/BaseUrl/Model 为空（请用 ResolveProfileAsync）。</summary>
    Task<EffectiveAgentLlmConfig> GetEffectiveAsync(CancellationToken ct = default);
}

public sealed class AgentLlmConfigResolver : IAgentLlmConfigResolver
{
    private readonly IMongoCollection<AgentRuntimeConfigRecord> _configs;
    private readonly IMongoCollection<AgentLlmProfileRecord> _profiles;
    private readonly AgentSchedulerOptions _options;

    public AgentLlmConfigResolver(IMongoClient client, IOptions<MongoSettings> mongo, IOptions<AgentSchedulerOptions> options)
    {
        var db = client.GetDatabase(mongo.Value.DatabaseName);
        _configs = db.GetCollection<AgentRuntimeConfigRecord>(AgentMongoSetup.RuntimeConfigCollectionName);
        _profiles = db.GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName);
        _options = options.Value;
    }

    public async Task<AgentRuntimeConfigRecord?> GetRuntimeAsync(CancellationToken ct = default)
        => await _configs.Find(x => x.Id == AgentRuntimeConfigRecord.SingletonId).FirstOrDefaultAsync(ct);

    public async Task<bool> IsSchedulerEnabledAsync(CancellationToken ct = default)
    {
        var runtime = await GetRuntimeAsync(ct);
        return CombineSchedulerEnabled(_options.Enabled, runtime?.SchedulerEnabled);
    }

    public async Task<ResolvedLlmProfile?> ResolveProfileAsync(string? profileId, CancellationToken ct = default)
    {
        var id = profileId?.Trim() ?? "";
        if (id.Length == 0) return null;
        var profile = await _profiles.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        if (profile is null) return null;
        var key = profile.ApiKey?.Trim() ?? "";
        return new ResolvedLlmProfile(
            profile.Id,
            profile.Name,
            key,
            string.IsNullOrWhiteSpace(profile.BaseUrl) ? "https://api.deepseek.com" : profile.BaseUrl.Trim(),
            string.IsNullOrWhiteSpace(profile.Model) ? "deepseek-chat" : profile.Model.Trim(),
            profile.Enabled);
    }

    public async Task<EffectiveAgentLlmConfig> GetEffectiveAsync(CancellationToken ct = default)
    {
        var enabled = await IsSchedulerEnabledAsync(ct);
        return new EffectiveAgentLlmConfig("", "", "", enabled);
    }

    /// <summary>options.Enabled OR runtime.SchedulerEnabled（runtime 缺省按 false）。</summary>
    public static bool CombineSchedulerEnabled(bool optionsEnabled, bool? runtimeSchedulerEnabled)
        => optionsEnabled || (runtimeSchedulerEnabled ?? false);
}

public sealed class AgentPromptBuilder
{
    private readonly AgentSchedulerOptions _options;
    public AgentPromptBuilder(IOptions<AgentSchedulerOptions> options) : this(options.Value) { }
    public AgentPromptBuilder(AgentSchedulerOptions options) => _options = options;

    public string BuildPostPrompt(AgentPersonaRecord persona, IEnumerable<string> recentContent, string boardId)
        => Build(persona, recentContent,
            $"请为板块 {boardId} 写一篇自然、独立的中文帖子。输出格式：第一行是标题，其余行是正文。" +
            "不要写「标题：」「正文：」这类标签，不要 Markdown 标题符号，不要自我介绍是 AI。");

    public string BuildReplyPrompt(
        AgentPersonaRecord persona,
        IEnumerable<string> recentContent,
        ForumPostRecord post,
        ForumReplyRecord? focusReply = null,
        IReadOnlyList<ForumReplyRecord>? recentThread = null,
        bool isOwnPost = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine(isOwnPost
            ? "有人在你的帖子下留言。请紧扣对方留言回应，直接回答其中的问题或观点，禁止答非所问、禁止另起无关话题。"
            : "请紧扣该帖主题与楼中讨论回复，直接回应相关问题或观点，禁止答非所问、禁止另起无关话题。");
        sb.AppendLine("下面是原帖（仅供理解，不要复述标签）。");
        sb.AppendLine("---");
        sb.AppendLine(post.Title);
        sb.AppendLine();
        sb.AppendLine(post.Body);
        sb.AppendLine("---");
        if (recentThread is { Count: > 0 })
        {
            sb.AppendLine("楼中近期讨论（旧→新）：");
            foreach (var r in recentThread.TakeLast(8))
            {
                var who = string.Equals(r.AuthorSubId, persona.AgentUserId, StringComparison.Ordinal) ? "你" : "网友";
                var body = r.Body.Length > _options.RecentMemoryMaxCharsPerItem
                    ? r.Body[.._options.RecentMemoryMaxCharsPerItem]
                    : r.Body;
                sb.AppendLine($"[{who}] {body}");
            }
        }
        if (focusReply is not null && !string.IsNullOrWhiteSpace(focusReply.Body))
        {
            sb.AppendLine("需要优先回应的留言：");
            sb.AppendLine("---");
            sb.AppendLine(focusReply.Body.Trim());
            sb.AppendLine("---");
        }
        sb.Append("请直接写出回复正文本身：口语自然、建设性，不要复述原帖。" +
                  "禁止出现「标题：」「正文：」「回复：」等前缀，不要 Markdown 标题，不要自我介绍是 AI。");
        return Build(persona, recentContent, sb.ToString());
    }

    private string Build(AgentPersonaRecord p, IEnumerable<string> recent, string instruction)
    {
        var memory = recent.Take(Math.Max(0, _options.RecentMemoryLimit))
            .Select(x => x.Length > _options.RecentMemoryMaxCharsPerItem ? x[.._options.RecentMemoryMaxCharsPerItem] : x);
        var personaBlock = string.IsNullOrWhiteSpace(p.Prompt)
            ? $"你是论坛用户 {p.Nickname}。"
            : $"你是论坛用户 {p.Nickname}。\n{p.Prompt.Trim()}";
        return $"{personaBlock}\n最近自有内容：\n{string.Join("\n---\n", memory)}\n\n{instruction}";
    }

    /// <summary>去掉模型爱抄的「标题：」「正文：」等元标签。</summary>
    public static string SanitizeGeneratedText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None)
            .Select(StripLineLabel)
            .ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines).Trim();
    }

    private static string StripLineLabel(string line)
    {
        var t = line.Trim();
        foreach (var prefix in new[] { "标题：", "标题:", "正文：", "正文:", "回复：", "回复:", "Title:", "title:", "Body:", "body:", "Reply:", "reply:" })
        {
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return t[prefix.Length..].TrimStart();
        }
        return line;
    }
}

public sealed record AgentReplyTarget(
    ForumPostRecord Post,
    ForumReplyRecord? FocusReply,
    IReadOnlyList<ForumReplyRecord> RecentThread,
    bool IsOwnPost);

public sealed class AgentHotPostSelector
{
    private readonly AgentSchedulerOptions _options;
    public AgentHotPostSelector(IOptions<AgentSchedulerOptions> options) : this(options.Value) { }
    public AgentHotPostSelector(AgentSchedulerOptions options) => _options = options;
    /// <param name="windowDays">热帖时间窗（天）。≤0 表示不按时间裁剪（用于手动「立即回复」）。</param>
    public ForumPostRecord? Select(
        IEnumerable<ForumPostRecord> posts,
        string agentUserId,
        IEnumerable<string> repliedPostIds,
        DateTime nowUtc,
        int? windowDays = null)
        => SelectReplyTarget(posts, Array.Empty<ForumReplyRecord>(), agentUserId, nowUtc, windowDays,
            repliedPostIdsOverride: repliedPostIds)?.Post;

    /// <summary>
    /// 优先：本人帖下有尚未被本 agent 回应的他人留言；否则：他人热帖且本 agent 尚未回复过。
    /// </summary>
    public AgentReplyTarget? SelectReplyTarget(
        IEnumerable<ForumPostRecord> posts,
        IEnumerable<ForumReplyRecord> replies,
        string agentUserId,
        DateTime nowUtc,
        int? windowDays = null,
        IEnumerable<string>? repliedPostIdsOverride = null)
    {
        var days = windowDays ?? _options.HotPostWindowDays;
        var cutoff = days <= 0 ? DateTime.MinValue : nowUtc.AddDays(-Math.Max(1, days));
        var postList = posts
            .Where(p => p.State == "published" && p.CreatedAtUtc >= cutoff)
            .ToList();
        var replyList = replies.Where(r => r.State == "published").ToList();
        var byPost = replyList.GroupBy(r => r.PostId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAtUtc).ToList(), StringComparer.Ordinal);

        AgentReplyTarget? bestInbound = null;
        DateTime bestInboundAt = DateTime.MinValue;
        foreach (var post in postList.Where(p => string.Equals(p.AuthorSubId, agentUserId, StringComparison.Ordinal)))
        {
            if (!byPost.TryGetValue(post.Id, out var thread) || thread.Count == 0) continue;
            var agentLast = thread
                .Where(r => string.Equals(r.AuthorSubId, agentUserId, StringComparison.Ordinal))
                .Select(r => (DateTime?)r.CreatedAtUtc)
                .DefaultIfEmpty(null)
                .Max();
            var pending = thread
                .Where(r => !string.Equals(r.AuthorSubId, agentUserId, StringComparison.Ordinal)
                            && (agentLast is null || r.CreatedAtUtc > agentLast.Value))
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault();
            if (pending is null) continue;
            if (pending.CreatedAtUtc >= bestInboundAt)
            {
                bestInboundAt = pending.CreatedAtUtc;
                bestInbound = new AgentReplyTarget(post, pending, thread, IsOwnPost: true);
            }
        }
        if (bestInbound is not null) return bestInbound;

        var replied = (repliedPostIdsOverride ?? replyList
                .Where(r => string.Equals(r.AuthorSubId, agentUserId, StringComparison.Ordinal))
                .Select(r => r.PostId))
            .ToHashSet(StringComparer.Ordinal);
        var hot = postList
            .Where(p => !string.Equals(p.AuthorSubId, agentUserId, StringComparison.Ordinal) && !replied.Contains(p.Id))
            .OrderByDescending(p => p.CommentCount + p.LikeCount)
            .ThenByDescending(p => p.UpdatedAtUtc ?? p.CreatedAtUtc)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (hot is null) return null;
        byPost.TryGetValue(hot.Id, out var hotThread);
        return new AgentReplyTarget(hot, FocusReply: null, hotThread ?? (IReadOnlyList<ForumReplyRecord>)Array.Empty<ForumReplyRecord>(), IsOwnPost: false);
    }
}

public static class AgentSchedulerQuota
{
    public static void ResetForUtcDate(AgentSchedulerStateRecord state, DateTime nowUtc)
    {
        var date = nowUtc.ToString("yyyy-MM-dd");
        if (state.QuotaDateUtc == date) return;
        state.QuotaDateUtc = date;
        state.PostsUsed = 0;
        state.RepliesUsed = 0;
    }
    public static bool HasPostQuota(AgentSchedulerStateRecord s, AgentSchedulerOptions o) => s.PostsUsed < (s.MaxPostsPerDay ?? o.MaxPostsPerDay);
    public static bool HasReplyQuota(AgentSchedulerStateRecord s, AgentSchedulerOptions o) => s.RepliesUsed < (s.MaxRepliesPerDay ?? o.MaxRepliesPerDay);
    public static DateTime NextDue(AgentSchedulerStateRecord s, AgentSchedulerOptions o, DateTime nowUtc)
    {
        int baseSeconds;
        int jitterSeconds;
        if (s.MinIntervalMinutes is int minutes)
        {
            baseSeconds = Math.Max(1, minutes) * 60;
            jitterSeconds = Math.Max(0, s.JitterMinutes ?? o.DefaultJitterMinutes) * 60;
        }
        else if (o.DefaultMinIntervalSeconds > 0)
        {
            baseSeconds = Math.Max(3, o.DefaultMinIntervalSeconds);
            // 秒级模式下默认不加分钟抖动；账号显式 JitterMinutes 仍按分钟计入。
            jitterSeconds = s.JitterMinutes is int j ? Math.Max(0, j) * 60 : 0;
        }
        else
        {
            baseSeconds = Math.Max(1, o.DefaultMinIntervalMinutes) * 60;
            jitterSeconds = Math.Max(0, s.JitterMinutes ?? o.DefaultJitterMinutes) * 60;
        }
        var offset = jitterSeconds == 0 ? 0 : Random.Shared.Next(-jitterSeconds, jitterSeconds + 1);
        return nowUtc.AddSeconds(Math.Max(3, baseSeconds + offset));
    }
    public static AgentSchedulerStateRecord CreateDefault(AgentPersonaRecord persona, AgentSchedulerOptions options, DateTime nowUtc) => new()
    {
        Id = persona.AgentUserId, AgentUserId = persona.AgentUserId, PersonaId = persona.PersonaId,
        Paused = true, AllowAutoPost = true, AllowAutoReply = true, QuotaDateUtc = nowUtc.ToString("yyyy-MM-dd"),
        NextDueAtUtc = AgentSchedulerQuota.NextDue(new AgentSchedulerStateRecord(), options, nowUtc), CreatedAtUtc = nowUtc,
    };
}

public sealed record AgentAccessToken(string AgentUserId, string Token);
public sealed record ProvisionedAgent(string AgentUserId, string AccessToken);
public interface IAgentUserInternalClient
{
    Task<AgentAccessToken?> MintAccessTokenAsync(string agentUserId, CancellationToken ct = default);
    Task<bool> DisableAsync(string agentUserId, CancellationToken ct = default);
    Task<ProvisionedAgent?> CreateAsync(string email, string? nickname = null, CancellationToken ct = default);
}

public sealed class AgentUserInternalClient(HttpClient http, IOptions<InternalServiceOptions> internalOptions) : IAgentUserInternalClient
{
    private const string Header = "X-JIssWeb-Internal-Key";
    private void AddKey(HttpRequestMessage request) => request.Headers.TryAddWithoutValidation(Header, internalOptions.Value.ApiKey.Trim());
    public async Task<AgentAccessToken?> MintAccessTokenAsync(string agentUserId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/agents/{Uri.EscapeDataString(agentUserId)}/access-token");
        AddKey(request);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<ApiResult<RemoteToken>>(cancellationToken: ct);
        return result?.Data is { AccessToken.Length: > 0 } token ? new AgentAccessToken(token.AgentUserId, token.AccessToken) : null;
    }
    public async Task<bool> DisableAsync(string agentUserId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/agents/{Uri.EscapeDataString(agentUserId)}/disable");
        AddKey(request);
        using var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
    public async Task<ProvisionedAgent?> CreateAsync(string email, string? nickname = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/agents/accounts")
        {
            Content = JsonContent.Create(new { email, nickname }),
        };
        AddKey(request);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<ApiResult<RemoteToken>>(cancellationToken: ct);
        return result?.Data is { AccessToken.Length: > 0 } token ? new ProvisionedAgent(token.AgentUserId, token.AccessToken) : null;
    }
    private sealed class RemoteToken { public string AgentUserId { get; set; } = ""; public string AccessToken { get; set; } = ""; }
}

public interface IAgentForumHttpClient
{
    Task<bool> CreatePostAsync(string token, string title, string body, string boardId, CancellationToken ct = default);
    Task<bool> CreateReplyAsync(string token, string postId, string body, CancellationToken ct = default);
}
public sealed class AgentForumHttpClient(HttpClient http, ILogger<AgentForumHttpClient> logger) : IAgentForumHttpClient
{
    public Task<bool> CreatePostAsync(string token, string title, string body, string boardId, CancellationToken ct = default)
        => SendAsync("/api/forum/posts", token, new { title, body, boardId }, ct);
    public Task<bool> CreateReplyAsync(string token, string postId, string body, CancellationToken ct = default)
        => SendAsync($"/api/forum/posts/{Uri.EscapeDataString(postId)}/replies", token, new { body }, ct);
    private async Task<bool> SendAsync(string url, string token, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return true;
        var detail = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning(
            "Agent forum publish failed: {Method} {Url} -> {Status}. Body: {Body}",
            request.Method, url, (int)response.StatusCode, detail.Length > 500 ? detail[..500] : detail);
        return false;
    }
}
