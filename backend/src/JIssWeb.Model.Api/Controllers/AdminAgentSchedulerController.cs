using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Authorization;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

[ApiController, Route("api/forum/admin/agent-scheduler"), Authorize, RequireForumModerator]
public sealed class AdminAgentSchedulerController : ControllerBase
{
    private readonly IMongoCollection<AgentSchedulerStateRecord> _states;
    private readonly AgentSchedulerOptions _options;
    private readonly AgentSchedulerEngine _engine;
    public AdminAgentSchedulerController(IMongoClient client, IOptions<MongoSettings> mongo, IOptions<AgentSchedulerOptions> options, AgentSchedulerEngine engine)
    {
        _states = client.GetDatabase(mongo.Value.DatabaseName).GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName);
        _options = options.Value; _engine = engine;
    }
    [HttpGet("{personaId}/status")]
    public async Task<ActionResult<ApiResult<AgentSchedulerStatusDto>>> Status(string personaId, CancellationToken ct)
    {
        var state = await Find(personaId).FirstOrDefaultAsync(ct);
        if (state is null) return NotFound(ApiResult<AgentSchedulerStatusDto>.Fail("调度状态不存在", "PERSONA_NOT_FOUND"));
        AgentSchedulerQuota.ResetForUtcDate(state, DateTime.UtcNow);
        return Ok(ApiResult<AgentSchedulerStatusDto>.Ok(ToDto(state)));
    }
    [HttpPut("{personaId}/rules")]
    public async Task<ActionResult<ApiResult<AgentSchedulerStatusDto>>> Rules(string personaId, [FromBody] UpdateAgentSchedulerRulesRequest request, CancellationToken ct)
    {
        var state = await Find(personaId).FirstOrDefaultAsync(ct);
        if (state is null) return NotFound(ApiResult<AgentSchedulerStatusDto>.Fail("调度状态不存在", "PERSONA_NOT_FOUND"));
        if (request.MaxPostsPerDay is < 0 || request.MaxRepliesPerDay is < 0 || request.MinIntervalMinutes is <= 0 || request.JitterMinutes is < 0)
            return BadRequest(ApiResult<AgentSchedulerStatusDto>.Fail("调度规则无效", "INVALID_INPUT"));
        if (request.MaxPostsPerDay is not null) state.MaxPostsPerDay = request.MaxPostsPerDay;
        if (request.MaxRepliesPerDay is not null) state.MaxRepliesPerDay = request.MaxRepliesPerDay;
        if (request.MinIntervalMinutes is not null) state.MinIntervalMinutes = request.MinIntervalMinutes;
        if (request.JitterMinutes is not null) state.JitterMinutes = request.JitterMinutes;
        if (request.AllowAutoPost is not null) state.AllowAutoPost = request.AllowAutoPost.Value;
        if (request.AllowAutoReply is not null) state.AllowAutoReply = request.AllowAutoReply.Value;
        if (request.Paused is not null) state.Paused = request.Paused.Value;
        state.UpdatedAtUtc = DateTime.UtcNow;
        await _states.ReplaceOneAsync(x => x.Id == state.Id, state, cancellationToken: ct);
        return Ok(ApiResult<AgentSchedulerStatusDto>.Ok(ToDto(state)));
    }
    [HttpPost("{personaId}/pause")]
    public Task<ActionResult<ApiResult<AgentSchedulerStatusDto>>> Pause(string personaId, CancellationToken ct) => SetPause(personaId, true, ct);
    [HttpPost("{personaId}/resume")]
    public Task<ActionResult<ApiResult<AgentSchedulerStatusDto>>> Resume(string personaId, CancellationToken ct) => SetPause(personaId, false, ct);
    [HttpPost("{personaId}/trigger/post")]
    public Task<ActionResult<ApiResult<object>>> TriggerPost(string personaId, CancellationToken ct) => Trigger(personaId, AgentScheduledAction.Post, ct);
    [HttpPost("{personaId}/trigger/reply")]
    public Task<ActionResult<ApiResult<object>>> TriggerReply(string personaId, CancellationToken ct) => Trigger(personaId, AgentScheduledAction.Reply, ct);
    private async Task<ActionResult<ApiResult<AgentSchedulerStatusDto>>> SetPause(string personaId, bool paused, CancellationToken ct)
    {
        var state = await Find(personaId).FirstOrDefaultAsync(ct);
        if (state is null) return NotFound(ApiResult<AgentSchedulerStatusDto>.Fail("调度状态不存在", "PERSONA_NOT_FOUND"));
        state.Paused = paused; state.UpdatedAtUtc = DateTime.UtcNow;
        await _states.ReplaceOneAsync(x => x.Id == state.Id, state, cancellationToken: ct);
        return Ok(ApiResult<AgentSchedulerStatusDto>.Ok(ToDto(state)));
    }
    private async Task<ActionResult<ApiResult<object>>> Trigger(string personaId, AgentScheduledAction action, CancellationToken ct)
    {
        var result = await _engine.TriggerAsync(personaId, action, ct);
        if (result.Succeeded) return Ok(ApiResult<object>.Ok(new { ok = true }));
        var code = result.Code ?? "SCHEDULER_FAILED";
        var message = code switch
        {
            "NO_REPLY_TARGET" => "暂无可回复的帖子",
            "EMPTY_GENERATION" => "模型未生成有效内容",
            "FORUM_PUBLISH_FAILED" => "论坛发布失败",
            "QUOTA_EXHAUSTED" => "今日额度已用尽",
            "ACTION_NOT_ALLOWED" => "该动作未开启",
            "LLM_CONFIG_UNAVAILABLE" => "LLM 配置不可用",
            _ => "智能体动作未执行",
        };
        return Conflict(ApiResult<object>.Fail(message, code));
    }
    private IFindFluent<AgentSchedulerStateRecord, AgentSchedulerStateRecord> Find(string personaId) => _states.Find(x => x.PersonaId == personaId.Trim());
    private AgentSchedulerStatusDto ToDto(AgentSchedulerStateRecord s) => new()
    {
        PersonaId = s.PersonaId, Paused = s.Paused, AllowAutoPost = s.AllowAutoPost, AllowAutoReply = s.AllowAutoReply,
        PostsUsed = s.PostsUsed, RepliesUsed = s.RepliesUsed, MaxPostsPerDay = s.MaxPostsPerDay ?? _options.MaxPostsPerDay,
        MaxRepliesPerDay = s.MaxRepliesPerDay ?? _options.MaxRepliesPerDay, NextDueAtUtc = s.NextDueAtUtc,
        MinIntervalMinutes = s.MinIntervalMinutes ?? _options.DefaultMinIntervalMinutes, JitterMinutes = s.JitterMinutes ?? _options.DefaultJitterMinutes,
    };
}
public sealed class UpdateAgentSchedulerRulesRequest { public int? MaxPostsPerDay { get; set; } public int? MaxRepliesPerDay { get; set; } public int? MinIntervalMinutes { get; set; } public int? JitterMinutes { get; set; } public bool? AllowAutoPost { get; set; } public bool? AllowAutoReply { get; set; } public bool? Paused { get; set; } }
public sealed class AgentSchedulerStatusDto { public string PersonaId { get; set; } = ""; public bool Paused { get; set; } public bool AllowAutoPost { get; set; } public bool AllowAutoReply { get; set; } public int PostsUsed { get; set; } public int RepliesUsed { get; set; } public int MaxPostsPerDay { get; set; } public int MaxRepliesPerDay { get; set; } public int PostsRemaining => Math.Max(0, MaxPostsPerDay - PostsUsed); public int RepliesRemaining => Math.Max(0, MaxRepliesPerDay - RepliesUsed); public DateTime? NextDueAtUtc { get; set; } public int MinIntervalMinutes { get; set; } public int JitterMinutes { get; set; } }
