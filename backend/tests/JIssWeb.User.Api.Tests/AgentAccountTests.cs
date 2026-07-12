using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using JIssWeb.User.Api.Authorization;
using JIssWeb.User.Api.Controllers;
using JIssWeb.User.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.User.Api.Tests;

public sealed class AgentAccountFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public Mock<ICaptchaVerifier> CaptchaVerifier { get; } = new();
    public string DatabaseName { get; private set; } = "";

    public const string InternalKey = "agent-account-test-key";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "agent_accounts_" + Guid.NewGuid().ToString("N");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("Jwt:Key", "test-jwt-key-must-be-32-chars-minimum!!");
            b.UseSetting("InternalService:ApiKey", InternalKey);
            b.UseSetting("Captcha:Enabled", "true");
            b.UseSetting("Captcha:SecretKey", "test-secret");
            b.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => MockRedis());
                services.RemoveAll(typeof(ICaptchaVerifier));
                services.AddSingleton<ICaptchaVerifier>(_ => CaptchaVerifier.Object);
            });
        });

        CaptchaVerifier
            .Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        Client = Factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }

    public static HttpRequestMessage WithInternalKey(HttpRequestMessage request, string key = InternalKey)
    {
        request.Headers.TryAddWithoutValidation(RequireInternalApiKeyAttribute.HeaderName, key);
        return request;
    }

    private static IConnectionMultiplexer MockRedis()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);
        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.KeyTimeToLiveAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((TimeSpan?)null);
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
        return mux.Object;
    }
}

[CollectionDefinition("AgentAccount")]
public sealed class AgentAccountCollection : ICollectionFixture<AgentAccountFixture>
{
}

[Collection("AgentAccount")]
public sealed class AgentAccountTests
{
    private readonly AgentAccountFixture _fx;

    public AgentAccountTests(AgentAccountFixture fx) => _fx = fx;

    [Fact]
    public async Task Missing_or_wrong_internal_key_returns_401()
    {
        var missing = await _fx.Client.PostAsJsonAsync("/api/internal/agents/accounts", new
        {
            email = $"agent-missing-{Guid.NewGuid():N}@internal.local",
            personaId = "missing-key",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        var wrongReq = AgentAccountFixture.WithInternalKey(
            new HttpRequestMessage(HttpMethod.Post, "/api/internal/agents/accounts")
            {
                Content = JsonContent.Create(new
                {
                    email = $"agent-wrong-{Guid.NewGuid():N}@internal.local",
                    personaId = "wrong-key",
                }),
            },
            "wrong-key");
        var wrong = await _fx.Client.SendAsync(wrongReq);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Valid_internal_key_creates_agent_and_returns_agent_token()
    {
        var email = $"agent-{Guid.NewGuid():N}@internal.local";
        var req = AgentAccountFixture.WithInternalKey(new HttpRequestMessage(HttpMethod.Post, "/api/internal/agents/accounts")
        {
            Content = JsonContent.Create(new { email, personaId = "persona-alpha" }),
        });

        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<ApiEnvelope<AgentAccountResponse>>();
        Assert.True(body!.Success);
        Assert.False(string.IsNullOrWhiteSpace(body.Data!.AgentUserId));
        Assert.False(string.IsNullOrWhiteSpace(body.Data.AccessToken));
        Assert.True(body.Data.AccessTokenExpiresAtUtc > DateTime.UtcNow);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(body.Data.AccessToken);
        Assert.Equal("agent", token.Claims.Single(c => c.Type == "accountType").Value);
        Assert.Equal(body.Data.AgentUserId, token.Claims.Single(c => c.Type == "sub").Value);
    }

    [Fact]
    public async Task Agent_email_cannot_login_with_password()
    {
        var email = $"agent-login-{Guid.NewGuid():N}@internal.local";
        var create = AgentAccountFixture.WithInternalKey(new HttpRequestMessage(HttpMethod.Post, "/api/internal/agents/accounts")
        {
            Content = JsonContent.Create(new { email }),
        });
        var createRes = await _fx.Client.SendAsync(create);
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        var login = await _fx.Client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "AnyPassword123",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<ApiEnvelope<object>>();
        Assert.Equal("LOGIN_FAILED", body!.Code);
    }

    [Fact]
    public async Task Human_login_token_includes_accountType_human()
    {
        var email = $"human-{Guid.NewGuid():N}@example.com";
        const string password = "Test@1234";
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = Convert.ToBase64String(
            Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), 100_000, HashAlgorithmName.SHA256, 32));

        var mongo = _fx.Factory.Services.GetRequiredService<IMongoClient>();
        await mongo.GetDatabase(_fx.DatabaseName).GetCollection<UserAccount>("users").InsertOneAsync(new UserAccount
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Email = email,
            PasswordSalt = salt,
            PasswordHash = hash,
            CreatedAtUtc = DateTime.UtcNow,
            EmailVerifiedAtUtc = DateTime.UtcNow,
            IsAgentAccount = false,
        });

        var login = await _fx.Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<ApiEnvelope<AuthTokenPairDto>>();
        Assert.True(body!.Success);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(body.Data!.AccessToken);
        Assert.Equal("human", token.Claims.Single(c => c.Type == "accountType").Value);
    }

    [Fact]
    public async Task Real_agent_token_bypasses_registration_captcha()
    {
        var create = AgentAccountFixture.WithInternalKey(new HttpRequestMessage(HttpMethod.Post, "/api/internal/agents/accounts")
        {
            Content = JsonContent.Create(new { email = $"agent-captcha-{Guid.NewGuid():N}@internal.local" }),
        });
        var createRes = await _fx.Client.SendAsync(create);
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<ApiEnvelope<AgentAccountResponse>>();

        _fx.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created!.Data!.AccessToken);
        var register = await _fx.Client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"human-after-agent-{Guid.NewGuid():N}@example.com",
            password = "Test@1234",
        });

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        _fx.CaptchaVerifier.Verify(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public string? Code { get; set; }
        public T? Data { get; set; }
    }

    private sealed class AgentAccountResponse
    {
        public string AgentUserId { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public DateTime AccessTokenExpiresAtUtc { get; set; }
    }

    private sealed class AuthTokenPairDto
    {
        public string AccessToken { get; set; } = "";
    }
}
