using System.Net;
using System.Net.Http.Json;
using JIssWeb.User.Api.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Mongo2Go;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.User.Api.Tests;

/// <summary>
/// 注册护栏的端点级测试：Mongo2Go 真库 + WebApplicationFactory 真 HTTP，
/// Redis 用字典语义假件以驱动真实计数累积。
/// </summary>
public sealed class RegistrationGateApiFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongo;
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public async Task InitializeAsync()
    {
        _mongo = MongoDbRunner.Start();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
        _mongo?.Dispose();
    }

    public string NewDbName() => "reg_gate_" + Guid.NewGuid().ToString("N");

    public HttpClient CreateClient(
        bool registrationEnabled,
        int dailyCap = 50,
        int perIpDailyCap = 3,
        string? dbName = null)
    {
        dbName ??= NewDbName();

        var fakeRedis = new FakeRedisForApi();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongo!.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", dbName);
            b.UseSetting("Jwt:Key", "test-jwt-key-must-be-32-chars-minimum!!");
            b.UseSetting("Registration:Enabled", registrationEnabled ? "true" : "false");
            b.UseSetting("Registration:DailyCap", dailyCap.ToString());
            b.UseSetting("Registration:PerIpDailyCap", perIpDailyCap.ToString());
            b.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => fakeRedis.Multiplexer);
            });
        });
        _factories.Add(factory);
        return factory.CreateClient();
    }

    public async Task SeedUnverifiedUserAsync(string dbName, string email)
    {
        var client = new MongoClient(_mongo!.ConnectionString);
        await client.GetDatabase(dbName).GetCollection<BsonDocument>("users").InsertOneAsync(new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId().ToString(),
            ["Email"] = email,
            ["PasswordSalt"] = "salt",
            ["PasswordHash"] = "hash",
            ["CreatedAtUtc"] = DateTime.UtcNow,
            ["EmailVerifiedAtUtc"] = BsonNull.Value,
        });
    }

    public async Task<long> CountUsersAsync(string dbName)
    {
        var client = new MongoClient(_mongo!.ConnectionString);
        return await client.GetDatabase(dbName).GetCollection<BsonDocument>("users").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
    }

    private sealed class FakeRedisForApi
    {
        private readonly Dictionary<string, long> _counters = new();

        public IConnectionMultiplexer Multiplexer { get; }

        public FakeRedisForApi()
        {
            var db = new Mock<IDatabase>();

            db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync((RedisKey key, long value, CommandFlags _) =>
              {
                  var k = key.ToString();
                  _counters[k] = _counters.GetValueOrDefault(k) + value;
                  return _counters[k];
              });

            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync((RedisKey key, CommandFlags _) =>
                  _counters.TryGetValue(key.ToString(), out var n) ? (RedisValue)n : RedisValue.Null);

            db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);
            db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);
            db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<DateTime?>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);

            var mux = new Mock<IConnectionMultiplexer>();
            mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
            Multiplexer = mux.Object;
        }
    }
}

[CollectionDefinition("RegistrationGateApi")]
public class RegistrationGateApiCollection : ICollectionFixture<RegistrationGateApiFixture> { }

[Collection("RegistrationGateApi")]
public sealed class RegistrationGateApiTests
{
    private readonly RegistrationGateApiFixture _fx;

    public RegistrationGateApiTests(RegistrationGateApiFixture fx) => _fx = fx;

    private sealed class ApiEnvelope
    {
        public bool Success { get; set; }
        public string? Code { get; set; }
    }

    private sealed class StatusEnvelope
    {
        public bool Success { get; set; }
        public StatusData? Data { get; set; }
    }

    private sealed class StatusData
    {
        public bool Enabled { get; set; }
        public int RemainingToday { get; set; }
    }

    private static object RegisterBody(string email) => new
    {
        email,
        password = "Test@1234",
    };

    private static string NewEmail() => $"user{Guid.NewGuid():N}@example.com";

    // ------------------------------------------------------------------
    // 总闸
    // ------------------------------------------------------------------

    [Fact]
    public async Task Closed_gate_rejects_registration_without_writing_user()
    {
        var db = _fx.NewDbName();
        var client = _fx.CreateClient(registrationEnabled: false, dbName: db);

        var res = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail()));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ApiEnvelope>();
        Assert.Equal("REGISTRATION_CLOSED", body!.Code);
        Assert.Equal(0, await _fx.CountUsersAsync(db));
    }

    [Fact]
    public async Task Closed_gate_does_not_mask_validation_error()
    {
        var client = _fx.CreateClient(registrationEnabled: false);

        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = NewEmail(),
            password = "weak",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ApiEnvelope>();
        Assert.Equal("WEAK_PASSWORD", body!.Code);
    }

    [Fact]
    public async Task Closed_gate_blocks_resend_branch_for_existing_unverified_account()
    {
        var db = _fx.NewDbName();
        var email = NewEmail();
        await _fx.SeedUnverifiedUserAsync(db, email);
        var client = _fx.CreateClient(registrationEnabled: false, dbName: db);

        var res = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ApiEnvelope>();
        Assert.Equal("REGISTRATION_CLOSED", body!.Code);
    }

    [Fact]
    public async Task Closed_gate_does_not_block_resend_verification_endpoint()
    {
        var db = _fx.NewDbName();
        var email = NewEmail();
        await _fx.SeedUnverifiedUserAsync(db, email);
        var client = _fx.CreateClient(registrationEnabled: false, dbName: db);

        var res = await client.PostAsJsonAsync("/api/auth/resend-verification", new { email });

        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("REGISTRATION_CLOSED", raw);
    }

    // ------------------------------------------------------------------
    // 状态端点
    // ------------------------------------------------------------------

    [Fact]
    public async Task Status_is_anonymous_and_reports_closed_gate()
    {
        var client = _fx.CreateClient(registrationEnabled: false);

        var res = await client.GetAsync("/api/auth/registration-status");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DailyCap", raw);
        Assert.DoesNotContain("PerIpDailyCap", raw);
        var body = await res.Content.ReadFromJsonAsync<StatusEnvelope>();
        Assert.True(body!.Success);
        Assert.False(body.Data!.Enabled);
        Assert.Equal(0, body.Data.RemainingToday);
    }

    [Fact]
    public async Task Status_reports_remaining_quota_after_success()
    {
        var client = _fx.CreateClient(registrationEnabled: true, dailyCap: 3);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail()))).StatusCode);

        var res = await client.GetAsync("/api/auth/registration-status");
        var body = await res.Content.ReadFromJsonAsync<StatusEnvelope>();
        Assert.True(body!.Data!.Enabled);
        Assert.Equal(2, body.Data.RemainingToday);
    }

    // ------------------------------------------------------------------
    // 日上限
    // ------------------------------------------------------------------

    [Fact]
    public async Task Global_cap_returns_429_with_retry_after()
    {
        var client = _fx.CreateClient(registrationEnabled: true, dailyCap: 1);

        var first = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail()));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail()));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<ApiEnvelope>();
        Assert.Equal("REGISTRATION_DAILY_CAP_REACHED", body!.Code);

        Assert.True(second.Headers.TryGetValues("Retry-After", out var values));
        Assert.True(int.TryParse(values!.First(), out var retryAfter));
        Assert.True(retryAfter > 0);
    }

    [Fact]
    public async Task Failed_validation_does_not_consume_daily_quota()
    {
        var client = _fx.CreateClient(registrationEnabled: true, dailyCap: 1);

        var bad = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = NewEmail(),
            password = "weak",
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var good = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail()));
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }

    [Fact]
    public async Task Successful_registration_consumes_quota()
    {
        var client = _fx.CreateClient(registrationEnabled: true, dailyCap: 1);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail()))).StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail()));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }
}
