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

[Collection("AgentPersona")]
public sealed class AgentSchedulerAdminTests
{
    private readonly AgentPersonaIntegrationFixture _fx;
    public AgentSchedulerAdminTests(AgentPersonaIntegrationFixture fx) => _fx = fx;
    private static HttpRequestMessage Request(HttpMethod method, string url, string role, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken($"user-{role}", role));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }
    private async Task<string> EnsureProfileAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        await _fx.Database().GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName)
            .InsertOneAsync(new AgentLlmProfileRecord
            {
                Id = id, Name = $"p-{id[..6]}", BaseUrl = "https://api.deepseek.com",
                ApiKey = "secret-1234", Model = "deepseek-chat", Enabled = true, CreatedAtUtc = DateTime.UtcNow,
            });
        return id;
    }

    [Fact]
    public async Task Llm_profile_masks_key_and_blocks_delete_when_in_use()
    {
        const string url = "/api/forum/admin/agent-llm-profiles";
        var create = await _fx.Client.SendAsync(Request(HttpMethod.Post, url, ForumRoleClaim.Admin, new
        {
            name = "DeepSeek A", baseUrl = "https://api.deepseek.com", model = "deepseek-chat", apiKey = "secret-1234",
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var profileId = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var get = await _fx.Client.SendAsync(Request(HttpMethod.Get, $"{url}/{profileId}", ForumRoleClaim.Admin));
        var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.True(json.GetProperty("apiKeyConfigured").GetBoolean());
        Assert.Equal("****1234", json.GetProperty("apiKeyMask").GetString());
        Assert.DoesNotContain("secret-1234", await get.Content.ReadAsStringAsync());

        var agentUserId = $"agent-{Guid.NewGuid():N}";
        await _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).InsertOneAsync(new AgentPersonaRecord
        {
            Id = agentUserId, AgentUserId = agentUserId, PersonaId = $"persona-{Guid.NewGuid():N}",
            Nickname = "Ada", Model = AgentModelIds.Deepseek, LlmProfileId = profileId, CreatedAtUtc = DateTime.UtcNow,
        });
        var del = await _fx.Client.SendAsync(Request(HttpMethod.Delete, $"{url}/{profileId}", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
        Assert.Equal("LLM_PROFILE_IN_USE", JsonDocument.Parse(await del.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Runtime_config_only_toggles_scheduler()
    {
        const string url = "/api/forum/admin/agent-runtime";
        var put = await _fx.Client.SendAsync(Request(HttpMethod.Put, url, ForumRoleClaim.Admin, new { schedulerEnabled = true }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var get = await _fx.Client.SendAsync(Request(HttpMethod.Get, url, ForumRoleClaim.Admin));
        var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.True(json.GetProperty("schedulerEnabled").GetBoolean());
        Assert.False(json.TryGetProperty("apiKeyConfigured", out _));
    }
    [Fact]
    public async Task Moderator_can_access_runtime_config()
    {
        var response = await _fx.Client.SendAsync(Request(HttpMethod.Get, "/api/forum/admin/agent-runtime", ForumRoleClaim.Moderator));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Member_cannot_access_runtime_config()
    {
        var response = await _fx.Client.SendAsync(Request(HttpMethod.Get, "/api/forum/admin/agent-runtime", ForumRoleClaim.Member));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Fact]
    public async Task Manual_trigger_returns_ACTION_NOT_ALLOWED_when_rule_is_disabled()
    {
        var personaId = $"persona-{Guid.NewGuid():N}";
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        var personas = _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        var states = _fx.Database().GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName);
        await personas.InsertOneAsync(new AgentPersonaRecord { Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId, Nickname = "Ada", Model = AgentModelIds.Deepseek, CreatedAtUtc = DateTime.UtcNow });
        await states.InsertOneAsync(new AgentSchedulerStateRecord { Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId, AllowAutoPost = false, AllowAutoReply = true, QuotaDateUtc = DateTime.UtcNow.ToString("yyyy-MM-dd"), CreatedAtUtc = DateTime.UtcNow });
        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, $"/api/forum/admin/agent-scheduler/{personaId}/trigger/post", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ACTION_NOT_ALLOWED", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Provision_creates_persona_and_scheduler_state()
    {
        var profileId = await EnsureProfileAsync();
        var personaId = $"persona-{Guid.NewGuid():N}";
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        _fx.AgentUsers.Setup(x => x.CreateAsync("ada@example.test", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProvisionedAgent(agentUserId, "test-token"));

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, "/api/forum/admin/agent-personas/provision", ForumRoleClaim.Admin, new
        {
            email = "ada@example.test", personaId, nickname = "Ada", model = "deepseek", llmProfileId = profileId,
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var state = await _fx.Database().GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName)
            .Find(x => x.Id == agentUserId).FirstOrDefaultAsync();
        Assert.NotNull(state);
        Assert.Equal(personaId, state!.PersonaId);
    }

    [Fact]
    public async Task Provision_auto_assigns_personaId_email_and_requires_llm_profile()
    {
        var profileId = await EnsureProfileAsync();
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        string? capturedEmail = null;
        _fx.AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((email, _, _) => capturedEmail = email)
            .ReturnsAsync(new ProvisionedAgent(agentUserId, "test-token"));

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, "/api/forum/admin/agent-personas/provision", ForumRoleClaim.Admin, new
        {
            nickname = "AutoAda", llmProfileId = profileId,
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var data = root.GetProperty("data");
        var personaId = data.GetProperty("personaId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(personaId));
        Assert.StartsWith("persona-", personaId);
        Assert.Equal(profileId, data.GetProperty("llmProfileId").GetString());
        Assert.Equal($"{personaId}@agents.local", capturedEmail);

        var persona = await _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .Find(x => x.Id == agentUserId).FirstOrDefaultAsync();
        Assert.NotNull(persona);
        Assert.Equal(personaId, persona!.PersonaId);
        Assert.Equal(profileId, persona.LlmProfileId);
    }

    [Fact]
    public async Task Delete_returns_AGENT_DISABLE_FAILED_when_user_disable_fails()
    {
        var personaId = $"persona-{Guid.NewGuid():N}";
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        await _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .InsertOneAsync(new AgentPersonaRecord
            {
                Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId,
                Nickname = "Ada", Model = AgentModelIds.Deepseek, State = AgentPersonaState.Active, CreatedAtUtc = DateTime.UtcNow,
            });
        _fx.AgentUsers.Setup(x => x.DisableAsync(agentUserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Delete, $"/api/forum/admin/agent-personas/{personaId}", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("AGENT_DISABLE_FAILED", root.GetProperty("code").GetString());

        var stillThere = await _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .Find(x => x.PersonaId == personaId).FirstOrDefaultAsync();
        Assert.NotNull(stillThere);
        _fx.AgentUsers.Setup(x => x.DisableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task Resume_does_not_enable_global_scheduler_flag()
    {
        var personaId = $"persona-{Guid.NewGuid():N}";
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        await _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .InsertOneAsync(new AgentPersonaRecord
            {
                Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId,
                Nickname = "Ada", Model = AgentModelIds.Deepseek, State = AgentPersonaState.Active, CreatedAtUtc = DateTime.UtcNow,
            });
        await _fx.Database().GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName)
            .InsertOneAsync(new AgentSchedulerStateRecord
            {
                Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId, Paused = true,
                QuotaDateUtc = DateTime.UtcNow.ToString("yyyy-MM-dd"), CreatedAtUtc = DateTime.UtcNow,
            });
        await _fx.Database().GetCollection<AgentRuntimeConfigRecord>(AgentMongoSetup.RuntimeConfigCollectionName)
            .ReplaceOneAsync(
                x => x.Id == AgentRuntimeConfigRecord.SingletonId,
                new AgentRuntimeConfigRecord { Id = AgentRuntimeConfigRecord.SingletonId, SchedulerEnabled = false },
                new ReplaceOptions { IsUpsert = true });

        var resume = await _fx.Client.SendAsync(Request(HttpMethod.Post, $"/api/forum/admin/agent-scheduler/{personaId}/resume", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        var status = JsonDocument.Parse(await resume.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.False(status.GetProperty("paused").GetBoolean());

        var runtime = await _fx.Database().GetCollection<AgentRuntimeConfigRecord>(AgentMongoSetup.RuntimeConfigCollectionName)
            .Find(x => x.Id == AgentRuntimeConfigRecord.SingletonId).FirstOrDefaultAsync();
        Assert.False(runtime!.SchedulerEnabled);
    }

    [Fact]
    public async Task Manual_trigger_returns_QUOTA_EXHAUSTED_when_daily_limit_reached()
    {
        var personaId = $"persona-{Guid.NewGuid():N}";
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        await _fx.Database().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .InsertOneAsync(new AgentPersonaRecord
            {
                Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId,
                Nickname = "Ada", Model = AgentModelIds.Deepseek, State = AgentPersonaState.Active, CreatedAtUtc = DateTime.UtcNow,
            });
        await _fx.Database().GetCollection<AgentSchedulerStateRecord>(AgentMongoSetup.SchedulerStateCollectionName)
            .InsertOneAsync(new AgentSchedulerStateRecord
            {
                Id = agentUserId, AgentUserId = agentUserId, PersonaId = personaId,
                AllowAutoPost = true, MaxPostsPerDay = 1, PostsUsed = 1,
                QuotaDateUtc = DateTime.UtcNow.ToString("yyyy-MM-dd"), CreatedAtUtc = DateTime.UtcNow,
            });

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, $"/api/forum/admin/agent-scheduler/{personaId}/trigger/post", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("QUOTA_EXHAUSTED", root.GetProperty("code").GetString());
    }
}
