using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using JIssWeb.User.Api.Options;
using JIssWeb.User.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Mongo2Go;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.User.Api.Tests;

// ---------------------------------------------------------------------------
// Fixture
// ---------------------------------------------------------------------------

public sealed class CaptchaRegistrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;
    private readonly List<WebApplicationFactory<Program>> _factories = [];
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    // exposed so tests can swap behaviour between pass / fail
    public Mock<ICaptchaVerifier> CaptchaVerifier { get; } = new();

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        var dbName = "captcha_reg_" + Guid.NewGuid().ToString("N");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", dbName);
            b.UseSetting("Jwt:Key", "test-jwt-key-must-be-32-chars-minimum!!");
            // mock Redis — register endpoint uses it for rate-limiting
            b.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
                // replace verifier with controllable mock
                services.RemoveAll(typeof(ICaptchaVerifier));
                services.AddSingleton<ICaptchaVerifier>(_ => CaptchaVerifier.Object);
            });
        });
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var f in _factories)
            await f.DisposeAsync();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }

    public HttpClient CreateClient(bool captchaEnabled, bool captchaPass)
    {
        CaptchaVerifier
            .Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(captchaPass);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner!.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", "captcha_reg_" + Guid.NewGuid().ToString("N"));
            b.UseSetting("Jwt:Key", "test-jwt-key-must-be-32-chars-minimum!!");
            b.UseSetting("Captcha:Enabled", captchaEnabled ? "true" : "false");
            b.UseSetting("Captcha:SecretKey", "test-secret");
            b.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => MockRedis());
                services.RemoveAll(typeof(ICaptchaVerifier));
                services.AddSingleton<ICaptchaVerifier>(_ => CaptchaVerifier.Object);
            });
        });
        _factories.Add(factory);
        return factory.CreateClient();
    }

    private static IConnectionMultiplexer MockRedis()
    {
        var db = new Mock<IDatabase>();
        // StringIncrementAsync → returns 1 (below RegisterPerMinuteLimit threshold of 5)
        db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(1L);
        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);
        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
        return mux.Object;
    }

    public static string MakeAgentJwt()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-jwt-key-must-be-32-chars-minimum!!"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "JIssWeb",
            audience: "JIssWeb",
            claims: [new Claim("sub", "agent-001"), new Claim("accountType", "agent")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

[CollectionDefinition("CaptchaRegistration")]
public class CaptchaRegistrationCollection : ICollectionFixture<CaptchaRegistrationFixture> { }

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[Collection("CaptchaRegistration")]
public sealed class CaptchaRegistrationTests
{
    private readonly CaptchaRegistrationFixture _fx;

    public CaptchaRegistrationTests(CaptchaRegistrationFixture fx) => _fx = fx;

    // 3.3 — token 缺失 → 400 CAPTCHA_REQUIRED
    [Fact]
    public async Task Missing_token_returns_CAPTCHA_REQUIRED_when_enabled()
    {
        var client = _fx.CreateClient(captchaEnabled: true, captchaPass: true);
        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "bot@example.com",
            password = "Test@1234",
            // captchaToken deliberately omitted
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ApiEnvelope>();
        Assert.Equal("CAPTCHA_REQUIRED", body!.Code);
    }

    // 3.4 — token 无效（verifier false）→ 400 CAPTCHA_INVALID
    [Fact]
    public async Task Invalid_token_returns_CAPTCHA_INVALID()
    {
        var client = _fx.CreateClient(captchaEnabled: true, captchaPass: false);
        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "bot2@example.com",
            password = "Test@1234",
            captchaToken = "bad-token",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ApiEnvelope>();
        Assert.Equal("CAPTCHA_INVALID", body!.Code);
    }

    // 3.5 — token 有效 → 200，注册正常推进
    [Fact]
    public async Task Valid_token_allows_registration()
    {
        var client = _fx.CreateClient(captchaEnabled: true, captchaPass: true);
        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"human{Guid.NewGuid():N}@example.com",
            password = "Test@1234",
            captchaToken = "valid-token",
        });
        // 200 OK with VERIFICATION_EMAIL_SENT (email sender is console in test env)
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // 3.6 — Enabled=false → 无 token 也能通过
    [Fact]
    public async Task Disabled_captcha_allows_registration_without_token()
    {
        var client = _fx.CreateClient(captchaEnabled: false, captchaPass: false);
        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"dev{Guid.NewGuid():N}@example.com",
            password = "Test@1234",
            // no captchaToken
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // 3.7 — accountType=agent JWT → 无 token 也能通过（桩）
    [Fact]
    public async Task Agent_claim_bypasses_captcha()
    {
        var client = _fx.CreateClient(captchaEnabled: true, captchaPass: false);
        var jwt = CaptchaRegistrationFixture.MakeAgentJwt();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"agent{Guid.NewGuid():N}@example.com",
            password = "Test@1234",
            // no captchaToken
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private sealed class ApiEnvelope
    {
        public bool Success { get; set; }
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}
