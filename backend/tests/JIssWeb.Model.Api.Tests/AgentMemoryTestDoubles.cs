using JIssWeb.Model.Api.Services;

namespace JIssWeb.Model.Api.Tests;

/// <summary>LLM 替身：按 SystemPrompt 区分「生成」与「记忆抽取」——生成返回固定发帖/回复文本，抽取返回固定 JSON。</summary>
internal sealed class StubLlmChatClient : ILlmChatClient
{
    public const string PostBody = "测试标题\n这是一篇测试正文。";
    public const string ExtractJson = "{\"userSentiment\":\"友好、乐于讨论\",\"topicStances\":[{\"topic\":\"人工智能\",\"stance\":\"乐观支持\"}],\"patternType\":\"style\",\"description\":\"稳定风格\"}";

    public Task<string> CompleteAsync(LlmChatRequest request, CancellationToken ct = default)
    {
        var text = request.SystemPrompt.Contains("记忆抽取器") ? ExtractJson : PostBody;
        return Task.FromResult(text);
    }
}

/// <summary>论坛发布替身：始终成功（不真正发 HTTP）。</summary>
internal sealed class StubForumClient : IAgentForumHttpClient
{
    public Task<bool> CreatePostAsync(string token, string title, string body, string boardId, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<bool> CreateReplyAsync(string token, string postId, string body, CancellationToken ct = default)
        => Task.FromResult(true);
}

/// <summary>用户内部客户端替身：再签 agent token 成功。</summary>
internal sealed class StubUserClient : IAgentUserInternalClient
{
    public Task<AgentAccessToken?> MintAccessTokenAsync(string agentUserId, CancellationToken ct = default)
        => Task.FromResult<AgentAccessToken?>(new(agentUserId, "stub-token"));
    public Task<bool> DisableAsync(string agentUserId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<ProvisionedAgent?> CreateAsync(string email, string? nickname = null, CancellationToken ct = default) => Task.FromResult<ProvisionedAgent?>(null);
}
