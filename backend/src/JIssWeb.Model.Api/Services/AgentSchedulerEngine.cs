using JIssWeb.Common.Options;
using System.Linq;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public enum AgentScheduledAction { Post, Reply }
public sealed record AgentSchedulerRunResult(bool Succeeded, string? Code = null);

public sealed class AgentSchedulerEngine
{
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<AgentSchedulerStateRecord> _states;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly AgentSchedulerOptions _options;
    private readonly IAgentLlmConfigResolver _config;
    private readonly ILlmChatClient _llm;
    private readonly AgentPromptBuilder _prompts;
    private readonly AgentHotPostSelector _hotPosts;
    private readonly IAgentUserInternalClient _users;
    private readonly IAgentForumHttpClient _forum;
    private readonly AgentMemoryUpdater _memory;
    private readonly ILogger<AgentSchedulerEngine> _logger;

    public AgentSchedulerEngine(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions, IOptions<AgentSchedulerOptions> options,
        IAgentLlmConfigResolver config, ILlmChatClient llm, AgentPromptBuilder prompts, AgentHotPostSelector hotPosts,
        AgentMemoryUpdater memory, IAgentUserInternalClient users, IAgentForumHttpClient forum, ILogger<AgentSchedulerEngine> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _states = db.GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _options = options.Value; _config = config; _llm = llm; _prompts = prompts; _hotPosts = hotPosts;
        _memory = memory; _users = users; _forum = forum; _logger = logger;
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        if (!await _config.IsSchedulerEnabledAsync(ct)) return;
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var claim = await _states.FindOneAndUpdateAsync(
                x => !x.Paused && x.NextDueAtUtc != null && x.NextDueAtUtc <= now,
                Builders<AgentSchedulerStateRecord>.Update
                    .Set(x => x.NextDueAtUtc, now.AddSeconds(Math.Max(15, _options.TickScanSeconds * 2)))
                    .Set(x => x.UpdatedAtUtc, now),
                new FindOneAndUpdateOptions<AgentSchedulerStateRecord> { ReturnDocument = ReturnDocument.After }, ct);
            if (claim is null) return;
            var persona = await _personas.Find(x => x.Id == claim.AgentUserId && x.State == AgentPersonaState.Active).FirstOrDefaultAsync(ct);
            if (persona is null) await ScheduleNextAsync(claim, now, null, ct);
            else await RunAsync(persona, claim, null, true, ct);
        }
    }

    public async Task<AgentSchedulerRunResult> TriggerAsync(string personaId, AgentScheduledAction action, CancellationToken ct = default)
    {
        var state = await _states.Find(x => x.PersonaId == personaId).FirstOrDefaultAsync(ct);
        var persona = await _personas.Find(x => x.PersonaId == personaId && x.State == AgentPersonaState.Active).FirstOrDefaultAsync(ct);
        if (state is null || persona is null) return new(false, "PERSONA_NOT_FOUND");
        AgentSchedulerQuota.ResetForUtcDate(state, DateTime.UtcNow);
        if (action == AgentScheduledAction.Post && !state.AllowAutoPost) return new(false, "ACTION_NOT_ALLOWED");
        if (action == AgentScheduledAction.Reply && !state.AllowAutoReply) return new(false, "ACTION_NOT_ALLOWED");
        if (action == AgentScheduledAction.Post && !AgentSchedulerQuota.HasPostQuota(state, _options)) return new(false, "QUOTA_EXHAUSTED");
        if (action == AgentScheduledAction.Reply && !AgentSchedulerQuota.HasReplyQuota(state, _options)) return new(false, "QUOTA_EXHAUSTED");
        var profile = await _config.ResolveProfileAsync(persona.LlmProfileId, ct);
        if (profile is null || !profile.Enabled || string.IsNullOrWhiteSpace(profile.ApiKey))
            return new(false, "LLM_CONFIG_UNAVAILABLE");
        return await RunAsync(persona, state, action, false, ct, profile);
    }

    private async Task<AgentSchedulerRunResult> RunAsync(
        AgentPersonaRecord persona,
        AgentSchedulerStateRecord state,
        AgentScheduledAction? requested,
        bool automatic,
        CancellationToken ct,
        ResolvedLlmProfile? profile = null)
    {
        var now = DateTime.UtcNow;
        AgentSchedulerQuota.ResetForUtcDate(state, now);
        profile ??= await _config.ResolveProfileAsync(persona.LlmProfileId, ct);
        if (profile is null || !profile.Enabled || string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            _logger.LogWarning("Skip agent {PersonaId}: LLM profile missing, disabled, or without key.", persona.PersonaId);
            await ScheduleNextAsync(state, now, null, ct);
            return automatic ? new(true) : new(false, "LLM_CONFIG_UNAVAILABLE");
        }

        var action = requested ?? await SelectActionAsync(persona, state, now, ct);
        if (action is null) { await ScheduleNextAsync(state, now, null, ct); return new(false, "NO_ACTION_AVAILABLE"); }
        if (action == AgentScheduledAction.Post && !state.AllowAutoPost) return new(false, "ACTION_NOT_ALLOWED");
        if (action == AgentScheduledAction.Reply && !state.AllowAutoReply) return new(false, "ACTION_NOT_ALLOWED");
        if (action == AgentScheduledAction.Post && !AgentSchedulerQuota.HasPostQuota(state, _options)) return new(false, "QUOTA_EXHAUSTED");
        if (action == AgentScheduledAction.Reply && !AgentSchedulerQuota.HasReplyQuota(state, _options)) return new(false, "QUOTA_EXHAUSTED");
        try
        {
            var recent = await RecentContentAsync(persona.AgentUserId, ct);
            var token = await _users.MintAccessTokenAsync(persona.AgentUserId, ct);
            if (token is null) throw new InvalidOperationException("Agent token mint failed.");
            var outcome = action == AgentScheduledAction.Post
                ? await PublishPostAsync(persona, recent, token.Token, profile, ct)
                : await PublishReplyAsync(persona, recent, token.Token, profile, now, manual: !automatic, ct);
            if (!outcome.Ok)
            {
                _logger.LogWarning(
                    "Agent scheduler publish skipped for {PersonaId}: {Code}",
                    persona.PersonaId, outcome.Code ?? "UNKNOWN");
                await ScheduleNextAsync(state, now, outcome.Code, ct);
                return new(false, outcome.Code ?? "SCHEDULER_FAILED");
            }
            if (action == AgentScheduledAction.Post) state.PostsUsed++; else state.RepliesUsed++;
            await ScheduleNextAsync(state, now, null, ct);
            try
            {
                await _memory.UpdateAsync(persona, action, outcome.TargetUserSub, outcome.Body ?? "", outcome.Board ?? "general", profile, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Memory update failed post-publish for {PersonaId}.", persona.PersonaId);
            }
            return new(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent scheduler failed for {PersonaId}", persona.PersonaId);
            await ScheduleNextAsync(state, now, ex.GetType().Name, ct);
            return new(false, "SCHEDULER_FAILED");
        }
    }

    private readonly record struct PublishOutcome(bool Ok, string? Code = null, string? Body = null, string? Board = null, string? TargetUserSub = null);

    private async Task<AgentScheduledAction?> SelectActionAsync(AgentPersonaRecord persona, AgentSchedulerStateRecord state, DateTime now, CancellationToken ct)
    {
        if (state.AllowAutoReply && AgentSchedulerQuota.HasReplyQuota(state, _options))
        {
            var windowDays = Math.Max(1, _options.HotPostWindowDays);
            var candidates = await _posts.Find(x => x.State == "published" && x.CreatedAtUtc >= now.AddDays(-windowDays)).ToListAsync(ct);
            var candidateIds = candidates.Select(p => p.Id).ToList();
            var replies = candidateIds.Count == 0
                ? new List<ForumReplyRecord>()
                : await _replies.Find(x => candidateIds.Contains(x.PostId) && x.State == "published").ToListAsync(ct);
            if (_hotPosts.SelectReplyTarget(candidates, replies, persona.AgentUserId, now, windowDays) is not null)
                return AgentScheduledAction.Reply;
        }
        return state.AllowAutoPost && AgentSchedulerQuota.HasPostQuota(state, _options) ? AgentScheduledAction.Post : null;
    }

    private async Task<PublishOutcome> PublishPostAsync(AgentPersonaRecord persona, IReadOnlyList<string> recent, string token, ResolvedLlmProfile profile, CancellationToken ct)
    {
        var board = _options.AllowedBoardIds.FirstOrDefault() ?? "general";
        AgentMemoryContext? mem = _options.MemoryEnabled
            ? new AgentMemoryContext(persona.RelationshipMemory, persona.StanceLog, null)
            : null;
        var generated = await _llm.CompleteAsync(new("你是中文论坛写作助手。", _prompts.BuildPostPrompt(persona, recent, board, mem), profile.ApiKey, profile.BaseUrl, profile.Model), ct);
        var (title, body) = SplitPost(generated);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            return new(false, "EMPTY_GENERATION");
        var ok = await _forum.CreatePostAsync(token, title, body, board, ct);
        return ok ? new(true, Body: body, Board: board) : new(false, "FORUM_PUBLISH_FAILED");
    }

    private async Task<PublishOutcome> PublishReplyAsync(
        AgentPersonaRecord persona,
        IReadOnlyList<string> recent,
        string token,
        ResolvedLlmProfile profile,
        DateTime now,
        bool manual,
        CancellationToken ct)
    {
        // 手动「立即回复」放宽时间窗，避免站内仅有旧帖时无法试跑；自动调度仍用 HotPostWindowDays。
        var windowDays = manual ? 0 : Math.Max(1, _options.HotPostWindowDays);
        var candidates = windowDays <= 0
            ? await _posts.Find(x => x.State == "published").ToListAsync(ct)
            : await _posts.Find(x => x.State == "published" && x.CreatedAtUtc >= now.AddDays(-windowDays)).ToListAsync(ct);
        var candidateIds = candidates.Select(p => p.Id).ToList();
        var replies = candidateIds.Count == 0
            ? new List<ForumReplyRecord>()
            : await _replies.Find(x => candidateIds.Contains(x.PostId) && x.State == "published").ToListAsync(ct);
        var target = _hotPosts.SelectReplyTarget(candidates, replies, persona.AgentUserId, now, windowDays);
        if (target is null) return new(false, "NO_REPLY_TARGET");
        AgentMemoryContext? mem = _options.MemoryEnabled
            ? new AgentMemoryContext(
                persona.RelationshipMemory,
                persona.StanceLog,
                await GetRecentDialoguesAsync(persona.AgentUserId, target.Post.AuthorSubId, _options.RecentMemoryLimit, ct))
            : null;
        var prompt = _prompts.BuildReplyPrompt(
            persona, recent, target.Post, target.FocusReply, target.RecentThread, target.IsOwnPost, mem);
        var generated = await _llm.CompleteAsync(new("你是中文论坛写作助手。", prompt, profile.ApiKey, profile.BaseUrl, profile.Model), ct);
        var body = AgentPromptBuilder.SanitizeGeneratedText(generated);
        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("Agent reply empty after sanitize for {PersonaId}; raw length={Len}", persona.PersonaId, generated?.Length ?? 0);
            return new(false, "EMPTY_GENERATION");
        }
        var ok = await _forum.CreateReplyAsync(token, target.Post.Id, body, ct);
        return ok ? new(true, Body: body, Board: target.Post.Board, TargetUserSub: target.Post.AuthorSubId) : new(false, "FORUM_PUBLISH_FAILED");
    }

    private async Task<IReadOnlyList<string>> RecentContentAsync(string agentUserId, CancellationToken ct)
    {
        var posts = await _posts.Find(x => x.AuthorSubId == agentUserId && x.State == "published").SortByDescending(x => x.CreatedAtUtc).Project(x => x.Body).ToListAsync(ct);
        var replies = await _replies.Find(x => x.AuthorSubId == agentUserId && x.State == "published").SortByDescending(x => x.CreatedAtUtc).Project(x => x.Body).ToListAsync(ct);
        return posts.Concat(replies).Take(_options.RecentMemoryLimit).ToList();
    }

    /// <summary>取 agent 与目标用户「最近 ≤limit 条相关对话」：双向聚合 forum_replies 后按时间倒序。</summary>
    private async Task<IReadOnlyList<string>> GetRecentDialoguesAsync(string agentUserId, string targetUserSub, int limit, CancellationToken ct)
    {
        var agentPostIds = await _posts.Find(x => x.AuthorSubId == agentUserId && x.State == "published").Project(x => x.Id).ToListAsync(ct);
        var repliesOnAgentPosts = agentPostIds.Count == 0
            ? new List<ForumReplyRecord>()
            : await _replies.Find(r => agentPostIds.Contains(r.PostId) && r.AuthorSubId == targetUserSub && r.State == "published")
                .SortByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
        var targetPostIds = await _posts.Find(x => x.AuthorSubId == targetUserSub && x.State == "published").Project(x => x.Id).ToListAsync(ct);
        var repliesByAgent = targetPostIds.Count == 0
            ? new List<ForumReplyRecord>()
            : await _replies.Find(r => targetPostIds.Contains(r.PostId) && r.AuthorSubId == agentUserId && r.State == "published")
                .SortByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
        return repliesOnAgentPosts.Concat(repliesByAgent)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .Select(r => $"[{r.AuthorSubId}] {r.Body}")
            .ToList();
    }

    private async Task ScheduleNextAsync(AgentSchedulerStateRecord state, DateTime now, string? error, CancellationToken ct)
    {
        state.NextDueAtUtc = error is null ? AgentSchedulerQuota.NextDue(state, _options, now) : now.AddMinutes(Math.Min(60, 5 * Math.Max(1, state.ConsecutiveFailures + 1)));
        state.ConsecutiveFailures = error is null ? 0 : state.ConsecutiveFailures + 1;
        state.LastError = error; state.LastErrorAtUtc = error is null ? null : now; state.UpdatedAtUtc = now;
        await _states.ReplaceOneAsync(x => x.Id == state.Id, state, cancellationToken: ct);
    }

    private static (string Title, string Body) SplitPost(string generated)
    {
        var cleaned = AgentPromptBuilder.SanitizeGeneratedText(generated);
        var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var title = lines.FirstOrDefault() ?? "来自智能体的分享";
        var body = string.Join('\n', lines.Skip(1));
        title = AgentPromptBuilder.SanitizeGeneratedText(title);
        body = AgentPromptBuilder.SanitizeGeneratedText(body);
        if (string.IsNullOrWhiteSpace(body)) body = cleaned;
        return (title.Length > 120 ? title[..120] : title, body);
    }
}

public sealed class AgentSchedulerBackgroundService(AgentSchedulerEngine engine, IOptions<AgentSchedulerOptions> options, ILogger<AgentSchedulerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await engine.TickAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Agent scheduler tick failed."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.TickScanSeconds)), stoppingToken);
        }
    }
}
