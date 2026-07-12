using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

public sealed class AgentPersonaIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        var dbName = "agent_personas_" + Guid.NewGuid().ToString("N");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", dbName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
            });
        });
        Client = Factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }

    public IMongoDatabase Database()
    {
        var mongo = Factory.Services.GetRequiredService<IMongoClient>();
        var dbName = Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoSettings>>().Value.DatabaseName;
        return mongo.GetDatabase(dbName);
    }
}

[CollectionDefinition("AgentPersona")]
public sealed class AgentPersonaCollection : ICollectionFixture<AgentPersonaIntegrationFixture>
{
}

[Collection("AgentPersona")]
public sealed class AgentPersonaTests
{
    private const string BaseUrl = "/api/forum/admin/agent-personas";
    private readonly AgentPersonaIntegrationFixture _fx;

    public AgentPersonaTests(AgentPersonaIntegrationFixture fx) => _fx = fx;

    private static HttpRequestMessage AuthReq(HttpMethod method, string url, string role, string? jsonBody = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken($"{role}-user", role));
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return req;
    }

    private static HttpRequestMessage AdminReq(HttpMethod method, string url, string? jsonBody = null)
        => AuthReq(method, url, ForumRoleClaim.Admin, jsonBody);

    private static string CreateJson(string personaId, string agentUserId, string model = "doubao")
    {
        return JsonSerializer.Serialize(new
        {
            personaId,
            agentUserId,
            nickname = $"Agent {personaId}",
            model,
            personality = "calm",
            interests = new[] { "coding", "forum" },
            postingStyle = new { length = "short", emoji = "rare", catchphrases = new[] { "收到" } },
        });
    }

    private static async Task<JsonElement> ReadRootAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var root = await ReadRootAsync(response);
        return root.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Startup_ensures_agent_experience_weight_index()
    {
        var indexes = await _fx.Database()
            .GetCollection<BsonDocument>("agent_experiences")
            .Indexes
            .ListAsync();
        var docs = await indexes.ToListAsync();

        Assert.Contains(docs, d =>
            d.TryGetValue("key", out var key)
            && key.AsBsonDocument.TryGetValue("Weight", out var weight)
            && weight == -1);
    }

    [Fact]
    public async Task Member_and_moderator_cannot_manage_personas()
    {
        var body = CreateJson($"persona-{Guid.NewGuid():N}", $"agent-{Guid.NewGuid():N}");

        var member = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, BaseUrl, ForumRoleClaim.Member, body));
        Assert.Equal(HttpStatusCode.Forbidden, member.StatusCode);

        var moderator = await _fx.Client.SendAsync(AuthReq(HttpMethod.Post, BaseUrl, ForumRoleClaim.Moderator, body));
        Assert.Equal(HttpStatusCode.Forbidden, moderator.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_list_get_update_and_delete_persona()
    {
        var personaId = $"persona-{Guid.NewGuid():N}";
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        var create = await _fx.Client.SendAsync(AdminReq(HttpMethod.Post, BaseUrl, CreateJson(personaId, agentUserId)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await ReadRootAsync(create);
        var data = created.GetProperty("data");
        Assert.Equal(agentUserId, data.GetProperty("id").GetString());
        Assert.Equal(agentUserId, data.GetProperty("agentUserId").GetString());
        Assert.Equal(personaId, data.GetProperty("personaId").GetString());
        Assert.Equal("active", data.GetProperty("state").GetString());
        Assert.Equal(1, data.GetProperty("generation").GetInt32());
        Assert.Equal(0, data.GetProperty("survivalDays").GetInt32());

        var stored = await _fx.Database()
            .GetCollection<BsonDocument>("agent_personas")
            .Find(new BsonDocument("_id", agentUserId))
            .FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal(0, stored!["RelationshipMemory"].AsBsonDocument.ElementCount);
        Assert.Equal(0, stored["StanceLog"].AsBsonDocument.ElementCount);

        var list = await _fx.Client.SendAsync(AdminReq(HttpMethod.Get, BaseUrl));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listRoot = await ReadRootAsync(list);
        Assert.Contains(listRoot.GetProperty("data").EnumerateArray(), x =>
            x.GetProperty("personaId").GetString() == personaId);

        var get = await _fx.Client.SendAsync(AdminReq(HttpMethod.Get, $"{BaseUrl}/{personaId}"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var patch = JsonSerializer.Serialize(new
        {
            nickname = "Updated Agent",
            model = "deepseek",
            state = "archived",
            interests = new[] { "updated" },
        });
        var update = await _fx.Client.SendAsync(AdminReq(HttpMethod.Put, $"{BaseUrl}/{personaId}", patch));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadRootAsync(update);
        Assert.Equal("Updated Agent", updated.GetProperty("data").GetProperty("nickname").GetString());
        Assert.Equal("deepseek", updated.GetProperty("data").GetProperty("model").GetString());
        Assert.Equal("archived", updated.GetProperty("data").GetProperty("state").GetString());

        var delete = await _fx.Client.SendAsync(AdminReq(HttpMethod.Delete, $"{BaseUrl}/{personaId}"));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var afterDelete = await _fx.Client.SendAsync(AdminReq(HttpMethod.Get, $"{BaseUrl}/{personaId}"));
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Duplicate_personaId_returns_PERSONA_ID_EXISTS()
    {
        var personaId = $"persona-{Guid.NewGuid():N}";
        var first = await _fx.Client.SendAsync(AdminReq(HttpMethod.Post, BaseUrl, CreateJson(personaId, $"agent-{Guid.NewGuid():N}")));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _fx.Client.SendAsync(AdminReq(HttpMethod.Post, BaseUrl, CreateJson(personaId, $"agent-{Guid.NewGuid():N}")));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("PERSONA_ID_EXISTS", await ReadCodeAsync(second));
    }

    [Fact]
    public async Task Duplicate_agentUserId_returns_AGENT_USER_ALREADY_BOUND()
    {
        var agentUserId = $"agent-{Guid.NewGuid():N}";
        var first = await _fx.Client.SendAsync(AdminReq(HttpMethod.Post, BaseUrl, CreateJson($"persona-{Guid.NewGuid():N}", agentUserId)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _fx.Client.SendAsync(AdminReq(HttpMethod.Post, BaseUrl, CreateJson($"persona-{Guid.NewGuid():N}", agentUserId)));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("AGENT_USER_ALREADY_BOUND", await ReadCodeAsync(second));
    }

    [Fact]
    public async Task Invalid_model_is_rejected_on_create_and_update()
    {
        var create = await _fx.Client.SendAsync(AdminReq(
            HttpMethod.Post,
            BaseUrl,
            CreateJson($"persona-{Guid.NewGuid():N}", $"agent-{Guid.NewGuid():N}", "gpt")));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal("INVALID_MODEL", await ReadCodeAsync(create));

        var personaId = $"persona-{Guid.NewGuid():N}";
        var ok = await _fx.Client.SendAsync(AdminReq(HttpMethod.Post, BaseUrl, CreateJson(personaId, $"agent-{Guid.NewGuid():N}")));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var update = await _fx.Client.SendAsync(AdminReq(HttpMethod.Put, $"{BaseUrl}/{personaId}", "{\"model\":\"gpt\"}"));
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal("INVALID_MODEL", await ReadCodeAsync(update));
    }
}
