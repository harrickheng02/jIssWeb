using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Moq;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

public sealed class AgentMemoryPromptTests
{
    private static AgentPersonaRecord Persona() => new() { Nickname = "Ada", Prompt = "冷静、爱聊数学。" };
    private static AgentMemoryContext Mem() => new(
        new Dictionary<string, string> { ["user-x"] = "友好、乐于讨论" },
        new Dictionary<string, string> { ["人工智能"] = "乐观支持" },
        new List<string> { "[user-x] 你觉得AI怎么样？" });

    [Fact]
    public void U1_null_memory_does_not_inject_segments_and_keeps_baseline()
    {
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildPostPrompt(Persona(), new[] { "近期内容" }, "general");
        Assert.DoesNotContain("你的人际记忆：", prompt);
        Assert.DoesNotContain("你的立场记忆：", prompt);
        Assert.DoesNotContain("你与该用户的近期对话：", prompt);
        Assert.DoesNotContain("性格：", prompt);
        Assert.Contains("近期内容", prompt);
    }

    [Fact]
    public void U2_post_prompt_includes_memory_segments()
    {
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildPostPrompt(Persona(), new[] { "近期内容" }, "general", Mem());
        Assert.Contains("你的人际记忆：", prompt);
        Assert.Contains("user-x: 友好、乐于讨论", prompt);
        Assert.Contains("你的立场记忆：", prompt);
        Assert.Contains("人工智能: 乐观支持", prompt);
    }

    [Fact]
    public void U4_reply_prompt_includes_recent_dialogues()
    {
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildReplyPrompt(Persona(), Array.Empty<string>(), new ForumPostRecord { Title = "原帖", Body = "正文" }, memory: Mem());
        Assert.Contains("你与该用户的近期对话：", prompt);
        Assert.Contains("[user-x] 你觉得AI怎么样？", prompt);
    }

    [Fact]
    public void U5_reply_prompt_omits_dialogue_segment_when_empty()
    {
        var ctx = new AgentMemoryContext(
            new Dictionary<string, string> { ["user-x"] = "友好" },
            new Dictionary<string, string>(),
            null);
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildReplyPrompt(Persona(), Array.Empty<string>(), new ForumPostRecord { Title = "原帖", Body = "正文" }, memory: ctx);
        Assert.DoesNotContain("你与该用户的近期对话：", prompt);
        Assert.Contains("你的人际记忆：", prompt);
    }

    [Fact]
    public void U7_dialogue_truncation_respects_limit()
    {
        var longDialogue = new string('X', 500);
        var ctx = new AgentMemoryContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new List<string> { $"[user-x] {longDialogue}" });
        var opts = new AgentSchedulerOptions { RecentMemoryMaxCharsPerItem = 50 };
        var prompt = new AgentPromptBuilder(opts)
            .BuildReplyPrompt(Persona(), Array.Empty<string>(), new ForumPostRecord { Title = "t", Body = "b" }, memory: ctx);
        Assert.Contains("你与该用户的近期对话：", prompt);
        Assert.DoesNotContain(longDialogue, prompt);
    }

    [Fact]
    public async Task Extractor_extracts_from_json_via_llm_stub()
    {
        var llm = new Mock<ILlmChatClient>();
        llm.Setup(x => x.CompleteAsync(It.IsAny<LlmChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"userSentiment\":\"友好\",\"topicStances\":[{\"topic\":\"AI\",\"stance\":\"支持\"}],\"patternType\":\"style\",\"description\":\"稳定\"}");
        var ex = new LlmMemoryExtractor(llm.Object, new Mock<Microsoft.Extensions.Logging.ILogger<LlmMemoryExtractor>>().Object);
        var r = await ex.ExtractAsync(new ResolvedLlmProfile("p", "n", "k", "u", "m", true), "正文", "general", default);
        Assert.NotNull(r);
        Assert.Equal("友好", r!.UserSentiment);
        Assert.Equal("AI", r.TopicStances[0].Topic);
        Assert.Equal("style", r.PatternType);
    }

    [Fact]
    public async Task Extractor_returns_null_on_failure()
    {
        var llm = new Mock<ILlmChatClient>();
        llm.Setup(x => x.CompleteAsync(It.IsAny<LlmChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var ex = new LlmMemoryExtractor(llm.Object, new Mock<Microsoft.Extensions.Logging.ILogger<LlmMemoryExtractor>>().Object);
        var r = await ex.ExtractAsync(new ResolvedLlmProfile("p", "n", "k", "u", "m", true), "正文", "general", default);
        Assert.Null(r);
    }
}
