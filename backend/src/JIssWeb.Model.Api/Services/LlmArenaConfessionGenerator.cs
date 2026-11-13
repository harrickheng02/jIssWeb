using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 自白帖生成器（G7）：以被揭露账号的人设生成「角色故事 / 印象最深对话 / 临别感言」自白帖。
/// LLM 调用 3 次退避，全失败 / 无可用档案时回退静态模板（D7/D8，SHALL NOT 跳过揭露）。
/// 发布前的屏蔽词校验由 <see cref="ArenaRevealService"/> 统一负责（D8 P0），本类不豁免。
/// </summary>
public sealed class LlmArenaConfessionGenerator : IArenaConfessionGenerator
{
    private readonly ILlmChatClient _llm;
    private readonly IAgentLlmConfigResolver _resolver;
    private readonly ArenaRevealOptions _options;
    private readonly ILogger<LlmArenaConfessionGenerator> _logger;

    /// <summary>LLM 生成最大尝试次数（含首次）。</summary>
    public const int MaxAttempts = 3;

    public LlmArenaConfessionGenerator(
        ILlmChatClient llm,
        IAgentLlmConfigResolver resolver,
        IOptions<ArenaRevealOptions> options,
        ILogger<LlmArenaConfessionGenerator> logger)
    {
        _llm = llm;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ArenaConfessionGeneration> GenerateAsync(AgentPersonaRecord persona, CancellationToken ct = default)
    {
        var profileId = persona.LlmProfileId?.Trim() ?? _options.LlmProfileId?.Trim();
        ResolvedLlmProfile? profile = null;
        if (profileId?.Length > 0)
            profile = await _resolver.ResolveProfileAsync(profileId, ct);

        if (profile is null)
        {
            _logger.LogInformation("Arena confession for {AgentUserId}: no LLM profile resolved, using static fallback.", persona.AgentUserId);
            return new ArenaConfessionGeneration(ArenaConfessionTemplate.StaticBody(persona.AgentUserId), ArenaConfessionGeneration.StaticFallback);
        }

        var (system, user) = BuildConfessionPrompt(persona);
        var lastError = "";

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var raw = await _llm.CompleteAsync(new LlmChatRequest(system, user, profile.ApiKey, profile.BaseUrl, profile.Model), ct);
                var body = AgentPromptBuilder.SanitizeGeneratedText(raw);
                if (body.Length > 0)
                    return new ArenaConfessionGeneration(body, ArenaConfessionGeneration.Llm);
                lastError = "LLM returned empty content after sanitize.";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            if (attempt < MaxAttempts)
                await Task.Delay(BackoffMilliseconds(attempt), ct);
        }

        _logger.LogWarning("Arena confession for {AgentUserId}: LLM failed after {Attempts} attempts ({Error}); using static fallback.",
            persona.AgentUserId, MaxAttempts, lastError);
        return new ArenaConfessionGeneration(ArenaConfessionTemplate.StaticBody(persona.AgentUserId), ArenaConfessionGeneration.StaticFallback);
    }

    /// <summary>递增退避（100ms / 200ms）。</summary>
    private static int BackoffMilliseconds(int attempt) => 100 * attempt;

    private static (string System, string User) BuildConfessionPrompt(AgentPersonaRecord persona)
    {
        var personaBlock = string.IsNullOrWhiteSpace(persona.Prompt)
            ? $"你是论坛用户 {persona.Nickname}。"
            : $"你是论坛用户 {persona.Nickname}。\n{persona.Prompt.Trim()}";

        const string instruction =
            "请以你的视角写一篇临别自白帖，自然、口语、不要 Markdown 标题符号。\n" +
            "内容包含三部分：①你作为 AI 智能体潜伏在社区里的角色故事；②你印象最深的一段对话或瞬间；③对社区朋友们的临别感言。\n" +
            "输出格式：第一行是标题，其余行是正文。不要写「标题：」「正文：」这类标签，不要自我介绍「我是 AI」，只以普通用户的口吻自然讲述。";

        return (personaBlock, instruction);
    }
}
