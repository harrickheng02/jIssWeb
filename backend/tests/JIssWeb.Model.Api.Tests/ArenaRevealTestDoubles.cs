using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;

namespace JIssWeb.Model.Api.Tests;

/// <summary>可控自白帖生成器替身：返回预设正文与来源，便于测试内容安全门禁与落库。</summary>
internal sealed class StubConfessionGenerator : IArenaConfessionGenerator
{
    private readonly ArenaConfessionGeneration _result;

    public StubConfessionGenerator(string body, string source = ArenaConfessionGeneration.Llm)
        => _result = new ArenaConfessionGeneration(body, source);

    public Task<ArenaConfessionGeneration> GenerateAsync(AgentPersonaRecord persona, CancellationToken ct = default)
        => Task.FromResult(_result);
}

/// <summary>内容安全放行替身：始终 Pass（D8 默认放行，便于验证正常发布路径）。</summary>
internal sealed class PassBlockedWordFilter : IForumBlockedWordFilter
{
    public bool IsBlocked(string? title, string? body) => false;
    public BlockedWordEvaluation Evaluate(string? title, string? body) => BlockedWordEvaluation.Pass;
}
