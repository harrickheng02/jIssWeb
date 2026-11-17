using System.Net;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Services;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// AI 版主阶段一：LLM 请求体参数化。分类任务需要确定性输出，故新增可选 Temperature / MaxTokens；
/// 未显式指定时必须保持既有 0.8 行为，不得影响调度器与经验提取。
/// </summary>
public class LlmChatRequestOptionsTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var payload = """
                          {"choices":[{"message":{"content":"{\"verdict\":\"ok\",\"confidence\":0.1}"}}]}
                          """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (DeepSeekChatClient Client, CapturingHandler Handler) CreateClient()
    {
        var handler = new CapturingHandler();
        return (new DeepSeekChatClient(new HttpClient(handler)), handler);
    }

    private static LlmChatRequest Request(double? temperature = null, int? maxTokens = null)
        => new("sys", "user", "key", "https://api.example.com", "deepseek-chat", temperature, maxTokens);

    [Fact]
    public async Task Default_temperature_remains_0_8()
    {
        var (client, handler) = CreateClient();
        await client.CompleteAsync(Request());

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal(0.8, doc.RootElement.GetProperty("temperature").GetDouble(), 3);
        Assert.False(doc.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task Explicit_temperature_is_forwarded()
    {
        var (client, handler) = CreateClient();
        await client.CompleteAsync(Request(temperature: 0));

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal(0d, doc.RootElement.GetProperty("temperature").GetDouble(), 3);
    }

    [Fact]
    public async Task Explicit_max_tokens_is_forwarded()
    {
        var (client, handler) = CreateClient();
        await client.CompleteAsync(Request(maxTokens: 128));

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal(128, doc.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task System_and_user_prompts_are_unchanged()
    {
        var (client, handler) = CreateClient();
        await client.CompleteAsync(Request());

        using var doc = JsonDocument.Parse(handler.Body!);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("sys", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("content").GetString());
    }
}
