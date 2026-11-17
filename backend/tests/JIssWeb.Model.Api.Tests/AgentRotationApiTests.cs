using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 账号轮换器管理面夹具：真实 DI 图 + Mongo2Go，仅替换 User.Api 账号客户端。
/// <c>AgentRotation:Enabled=true</c>；关闭态用例通过 <c>WithWebHostBuilder</c> 派生客户端。
/// </summary>
public sealed class AgentRotationApiFixture : IAsyncLifetime
{
    private Mongo2Go.MongoDbRunner? _runner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public Mock<IAgentUserInternalClient> AgentUsers { get; } = new();

    public async Task InitializeAsync()
    {
        _runner = Mongo2Go.MongoDbRunner.Start();
        var dbName = "agent_rotation_api_" + Guid.NewGuid().ToString("N");

        AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ProvisionedAgent($"agent-{Guid.NewGuid():N}", "test-token"));
        AgentUsers.Setup(x => x.DisableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _runner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", dbName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
            b.UseSetting("AgentScheduler:Enabled", "false");
            b.UseSetting("AgentRotation:Enabled", "true");
            b.UseSetting("AgentRotation:MinActiveAgents", "6");
            b.UseSetting("AgentRotation:MaxActiveAgents", "12");
            b.UseSetting("AgentRotation:MaxPerRun", "2");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
                services.RemoveAll<IAgentUserInternalClient>();
                services.AddSingleton<IAgentUserInternalClient>(AgentUsers.Object);
            });
        });
        Client = Factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _runner?.Dispose();
    }

    public HttpClient CreateDisabledClient()
        => Factory.WithWebHostBuilder(b => b.UseSetting("AgentRotation:Enabled", "false")).CreateClient();

    public IMongoDatabase Database()
    {
        var mongo = Factory.Services.GetRequiredService<IMongoClient>();
        var dbName = Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoSettings>>().Value.DatabaseName;
        return mongo.GetDatabase(dbName);
    }

    public async Task<string> EnsureProfileAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        await Database().GetCollection<AgentLlmProfileRecord>(AgentMongoSetup.LlmProfilesCollectionName)
            .InsertOneAsync(new AgentLlmProfileRecord
            {
                Id = id,
                Name = $"rot-api-{id[..8]}",
                BaseUrl = "https://api.deepseek.com",
                ApiKey = "test-key-1234",
                Model = "deepseek-chat",
                Enabled = true,
                CreatedAtUtc = DateTime.UtcNow,
            });
        return id;
    }
}

[CollectionDefinition("AgentRotationApi")]
public sealed class AgentRotationApiCollection : ICollectionFixture<AgentRotationApiFixture>
{
}

[Collection("AgentRotationApi")]
public sealed class AgentRotationApiTests
{
    private const string BaseUrl = "/api/forum/admin/agent-rotation";
    private readonly AgentRotationApiFixture _fx;

    public AgentRotationApiTests(AgentRotationApiFixture fx) => _fx = fx;

    private static HttpRequestMessage Request(HttpMethod method, string url, string role)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken($"rot-{role}", role));
        return request;
    }

    [Fact]
    public async Task Status_returns_live_water_level()
    {
        var response = await _fx.Client.SendAsync(Request(HttpMethod.Get, $"{BaseUrl}/status", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.True(data.GetProperty("enabled").GetBoolean());
        Assert.Equal(6, data.GetProperty("minActiveAgents").GetInt32());
        Assert.Equal(12, data.GetProperty("maxActiveAgents").GetInt32());
        Assert.Equal(2, data.GetProperty("maxPerRun").GetInt32());
        Assert.True(data.GetProperty("gap").GetInt64() > 0);
    }

    [Fact]
    public async Task Run_triggers_once_and_records_operator()
    {
        await _fx.EnsureProfileAsync();
        var response = await _fx.Client.SendAsync(Request(HttpMethod.Post, $"{BaseUrl}/run", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal("manual", data.GetProperty("trigger").GetString());
        Assert.StartsWith("manual:", data.GetProperty("runKey").GetString());
        Assert.Equal("rot-admin", data.GetProperty("triggeredBySub").GetString());
        Assert.Equal(2, data.GetProperty("createdCount").GetInt32());
        Assert.NotNull(data.GetProperty("completedAtUtc").GetString());
    }

    [Fact]
    public async Task Runs_lists_recent_records()
    {
        await _fx.EnsureProfileAsync();
        await _fx.Client.SendAsync(Request(HttpMethod.Post, $"{BaseUrl}/run", ForumRoleClaim.Admin));

        var response = await _fx.Client.SendAsync(Request(HttpMethod.Get, $"{BaseUrl}/runs?limit=5", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.True(data.GetArrayLength() >= 1);
        Assert.Equal("manual", data[0].GetProperty("trigger").GetString());
    }

    [Fact]
    public async Task Anonymous_is_rejected()
    {
        var response = await _fx.Client.GetAsync($"{BaseUrl}/status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Member_is_forbidden()
    {
        var response = await _fx.Client.SendAsync(Request(HttpMethod.Get, $"{BaseUrl}/status", ForumRoleClaim.Member));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Run_returns_ROTATION_DISABLED_when_switch_is_off()
    {
        using var client = _fx.CreateDisabledClient();
        var response = await client.SendAsync(Request(HttpMethod.Post, $"{BaseUrl}/run", ForumRoleClaim.Admin));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ROTATION_DISABLED", root.GetProperty("code").GetString());
    }
}
