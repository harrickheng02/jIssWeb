using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using MongoDB.Driver;
using Moq;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 账号轮换器 V0.3 · Phase 1 回归基线：把 <c>Provision</c> 的编排下沉到
/// <see cref="IAgentProvisioningService"/> 前后，HTTP 契约（状态码 + 错误码 + 补偿停用）必须逐字不变。
/// </summary>
[Collection("AgentPersona")]
public sealed class AgentProvisioningTests
{
    private const string ProvisionUrl = "/api/forum/admin/agent-personas/provision";
    private readonly AgentPersonaIntegrationFixture _fx;

    public AgentProvisioningTests(AgentPersonaIntegrationFixture fx) => _fx = fx;

    private static HttpRequestMessage Request(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken("provision-admin", ForumRoleClaim.Admin));
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<string> EnsureProfileAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        await _fx.Database().GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName)
            .InsertOneAsync(new AgentLlmProfileRecord
            {
                Id = id,
                Name = $"provision-{id[..8]}",
                BaseUrl = "https://api.deepseek.com",
                ApiKey = "test-key-1234",
                Model = "deepseek-chat",
                Enabled = true,
                CreatedAtUtc = DateTime.UtcNow,
            });
        return id;
    }

    [Fact]
    public async Task Provision_returns_LLM_PROFILE_NOT_FOUND_when_profile_is_missing()
    {
        _fx.AgentUsers.Invocations.Clear();
        _fx.AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProvisionedAgent($"agent-{Guid.NewGuid():N}", "token"));

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, ProvisionUrl, new
        {
            nickname = "NoProfile", llmProfileId = "missing-profile-id",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("LLM_PROFILE_NOT_FOUND", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Provision_returns_AGENT_PROVISION_FAILED_when_account_creation_fails()
    {
        var profileId = await EnsureProfileAsync();
        _fx.AgentUsers.Invocations.Clear();
        _fx.AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProvisionedAgent?)null);

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, ProvisionUrl, new
        {
            nickname = "NoAccount", llmProfileId = profileId,
        }));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("AGENT_PROVISION_FAILED", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Provision_returns_PERSONA_ID_EXISTS_when_persona_id_is_taken()
    {
        var profileId = await EnsureProfileAsync();
        var personaId = $"persona-{Guid.NewGuid():N}";
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        await _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .InsertOneAsync(new AgentPersonaRecord
            {
                Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId,
                Nickname = "Existing", Model = AgentModelIds.Deepseek, LlmProfileId = profileId,
                State = AgentPersonaState.Active, CreatedAtUtc = DateTime.UtcNow,
            });
        _fx.AgentUsers.Invocations.Clear();
        _fx.AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProvisionedAgent($"agent-{Guid.NewGuid():N}", "token"));

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, ProvisionUrl, new
        {
            personaId, nickname = "Duplicate", llmProfileId = profileId,
        }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("PERSONA_ID_EXISTS", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Provision_disables_new_account_when_persona_write_fails()
    {
        var profileId = await EnsureProfileAsync();
        var personaId = $"persona-{Guid.NewGuid():N}";
        var existingAgent = $"agent-{Guid.NewGuid():N}";
        await _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .InsertOneAsync(new AgentPersonaRecord
            {
                Id = existingAgent, AgentUserId = existingAgent, PersonaId = personaId,
                Nickname = "Existing", Model = AgentModelIds.Deepseek, LlmProfileId = profileId,
                State = AgentPersonaState.Active, CreatedAtUtc = DateTime.UtcNow,
            });

        var newAgent = $"agent-{Guid.NewGuid():N}";
        _fx.AgentUsers.Invocations.Clear();
        _fx.AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProvisionedAgent(newAgent, "token"));

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, ProvisionUrl, new
        {
            personaId, nickname = "Compensated", llmProfileId = profileId,
        }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        _fx.AgentUsers.Verify(x => x.DisableAsync(newAgent, It.IsAny<CancellationToken>()), Times.Once);
    }
}
