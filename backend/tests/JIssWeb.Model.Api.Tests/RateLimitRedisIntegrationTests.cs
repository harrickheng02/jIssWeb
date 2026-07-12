using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using Testcontainers.Redis;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// Integration tests for the Redis-backed distributed rate limiter.
/// Requires Docker (Testcontainers).
/// </summary>
public sealed class RateLimitRedisIntegrationTests : IAsyncLifetime
{
    private RedisContainer? _redis;
    private MongoDbRunner? _mongo;
    private string _redisConnectionString = "";

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().Build();
        await _redis.StartAsync();
        _redisConnectionString = _redis.GetConnectionString();

        _mongo = MongoDbRunner.Start();
    }

    public async Task DisposeAsync()
    {
        _mongo?.Dispose();
        if (_redis != null)
            await _redis.DisposeAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(
        Action<IWebHostBuilder>? configure = null)
    {
        var dbName = "rl_redis_" + Guid.NewGuid().ToString("N");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("Mongo:ConnectionString", _mongo!.ConnectionString);
                b.UseSetting("Mongo:DatabaseName", dbName);
                b.UseSetting("Forum:Boards:0:Id", "general");
                b.UseSetting("Forum:Boards:0:Title", "综合");
                b.UseSetting("Redis:ConnectionString", _redisConnectionString);
                b.UseSetting("Forum:RateLimit:UseRedis", "true");
                b.UseSetting("Forum:PostRateLimit:MaxPosts", "2");
                b.UseSetting("Forum:PostRateLimit:MaxReplies", "10");
                b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
                b.UseSetting("Forum:SearchRateLimit:MaxRequests", "2");
                b.UseSetting("Forum:SearchRateLimit:WindowSeconds", "60");
                b.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IUserSanctionClient>();
                    services.AddSingleton<IUserSanctionClient>(_ => new TestUserSanctionClient());
                });
                configure?.Invoke(b);
            });
    }

    private static HttpRequestMessage AuthPost(string url, string sub, object? body = null, string? accountType = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken(sub, accountType: accountType));
        return req;
    }

    // 5.2 — 跨"实例"语义：同一 key 两次调用后计数共享
    [Fact]
    public async Task RedisBackend_SharedQuota_AcrossCallsOnSameKey()
    {
        using var factory = CreateFactory();

        // Post MaxPosts times (limit=2) — all should succeed
        using var client = factory.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            var res = await client.SendAsync(AuthPost("/api/forum/posts", "redis-rl-shared",
                new { title = $"t{i}", body = $"b{i}", boardId = "general" }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // 3rd post should be rate limited — same Redis key counts across "instances"
        var limited = await client.SendAsync(AuthPost("/api/forum/posts", "redis-rl-shared",
            new { title = "t2", body = "b2", boardId = "general" }));
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        using var doc = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal("RATE_LIMITED", doc.RootElement.GetProperty("code").GetString());
    }

    // 5.3 — Redis 断连时 fail-open（请求正常放行）
    [Fact]
    public async Task RedisBackend_FailOpen_WhenRedisUnavailable()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("Mongo:ConnectionString", _mongo!.ConnectionString);
                b.UseSetting("Mongo:DatabaseName", "rl_failopen_" + Guid.NewGuid().ToString("N"));
                b.UseSetting("Forum:Boards:0:Id", "general");
                b.UseSetting("Forum:Boards:0:Title", "综合");
                b.UseSetting("Forum:RateLimit:UseRedis", "true");
                b.UseSetting("Forum:PostRateLimit:MaxPosts", "1");
                b.UseSetting("Forum:PostRateLimit:WindowSeconds", "60");
                b.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IUserSanctionClient>();
                    services.AddSingleton<IUserSanctionClient>(_ => new TestUserSanctionClient());
                    // Override rate limit backend with fail-open RedisRateLimitBackend (null mux)
                    services.RemoveAll<IForumRateLimitBackend>();
                    services.AddSingleton<IForumRateLimitBackend>(_ =>
                        new RedisRateLimitBackend(
                            null,   // null mux → fail-open
                            Microsoft.Extensions.Options.Options.Create(new JIssWeb.Common.Options.RedisSettings()),
                            Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisRateLimitBackend>.Instance));
                });
            });

        using var client = factory.CreateClient();

        // With fail-open, all posts succeed regardless of quota
        for (var i = 0; i < 3; i++)
        {
            var res = await client.SendAsync(AuthPost("/api/forum/posts", "fail-open-user",
                new { title = $"t{i}", body = $"b{i}", boardId = "general" }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
    }

    // 5.4 — Agent 账号使用独立 key，不影响人类用户计数
    [Fact]
    public async Task AgentAccount_UsesIsolatedKey_DoesNotAffectHumanQuota()
    {
        const string humanSub = "redis-rl-human";
        const string agentSub = "redis-rl-agent";

        using var factory = CreateFactory(b =>
        {
            b.UseSetting("Forum:PostRateLimit:MaxPosts", "2");
            b.UseSetting("Forum:RateLimit:AgentPostMultiplier", "3");
        });
        using var client = factory.CreateClient();

        // Agent posts — should use agent:post:{sub} key, not post:{humanSub}
        for (var i = 0; i < 3; i++)
        {
            var agentRes = await client.SendAsync(AuthPost(
                "/api/forum/posts", agentSub,
                new { title = $"agent-t{i}", body = $"b{i}", boardId = "general" },
                accountType: "agent"));
            Assert.Equal(HttpStatusCode.OK, agentRes.StatusCode);
        }

        // Human posts — still have their full quota untouched by agent activity
        for (var i = 0; i < 2; i++)
        {
            var humanRes = await client.SendAsync(AuthPost(
                "/api/forum/posts", humanSub,
                new { title = $"human-t{i}", body = $"b{i}", boardId = "general" }));
            Assert.Equal(HttpStatusCode.OK, humanRes.StatusCode);
        }

        // Human 3rd post → should be rate limited (human quota = MaxPosts = 2)
        var limited = await client.SendAsync(AuthPost(
            "/api/forum/posts", humanSub,
            new { title = "human-t2", body = "b", boardId = "general" }));
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
    }

    // 5.5 — Agent 账号搜索不消耗限流配额，人类用户正常计数
    [Fact]
    public async Task AgentAccount_SearchBypassesRateLimit_HumanStillLimited()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Human exhausts search quota (MaxRequests=2)
        for (var i = 0; i < 2; i++)
        {
            var res = await client.GetAsync("/api/forum/posts?q=test");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        var humanLimited = await client.GetAsync("/api/forum/posts?q=test");
        Assert.Equal((HttpStatusCode)429, humanLimited.StatusCode);

        // Agent search — bypasses rate limit regardless
        var agentReq = new HttpRequestMessage(HttpMethod.Get, "/api/forum/posts?q=test");
        agentReq.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken("agent-searcher", accountType: "agent"));
        var agentRes = await client.SendAsync(agentReq);
        Assert.Equal(HttpStatusCode.OK, agentRes.StatusCode);
    }
}
