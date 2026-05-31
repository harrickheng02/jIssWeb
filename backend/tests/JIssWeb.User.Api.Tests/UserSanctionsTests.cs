using System.Net;
using System.Net.Http.Json;
using JIssWeb.User.Api.Models;
using JIssWeb.User.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Mongo2Go;
using MongoDB.Driver;

namespace JIssWeb.User.Api.Tests;

public sealed class UserSanctionsIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";
    public const string InternalKey = "test-internal-key";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "user_san_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("InternalService:ApiKey", InternalKey);
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

    public HttpRequestMessage WithKey(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("X-JIssWeb-Internal-Key", InternalKey);
        return req;
    }
}

[CollectionDefinition("UserSanctions")]
public class UserSanctionsCollection : ICollectionFixture<UserSanctionsIntegrationFixture>
{
}

[Collection("UserSanctions")]
public sealed class UserSanctionsTests : IClassFixture<UserSanctionsIntegrationFixture>
{
    private readonly UserSanctionsIntegrationFixture _fx;

    public UserSanctionsTests(UserSanctionsIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task Missing_internal_key_returns_401()
    {
        var r = await _fx.Client.GetAsync("/api/internal/users/u1/forum-sanction-status");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Active_mute_returns_isMuted_true()
    {
        var create = _fx.WithKey(new HttpRequestMessage(HttpMethod.Post, "/api/internal/users/u1/sanctions")
        {
            Content = JsonContent.Create(new
            {
                type = UserSanctionTypes.Mute,
                reason = "test",
                operatorSub = "mod-1",
                durationPreset = "24h",
            }),
        });
        await _fx.Client.SendAsync(create);

        var status = _fx.WithKey(new HttpRequestMessage(HttpMethod.Get, "/api/internal/users/u1/forum-sanction-status"));
        var r = await _fx.Client.SendAsync(status);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<ApiEnvelope<ForumSanctionStatusDto>>();
        Assert.True(body!.Data!.IsMuted);
        Assert.NotNull(body.Data.MutedUntilUtc);
    }

    [Fact]
    public async Task Revoke_clears_mute()
    {
        var create = _fx.WithKey(new HttpRequestMessage(HttpMethod.Post, "/api/internal/users/u2/sanctions")
        {
            Content = JsonContent.Create(new
            {
                type = UserSanctionTypes.Mute,
                reason = "test",
                operatorSub = "mod-1",
                durationPreset = "24h",
            }),
        });
        var createRes = await _fx.Client.SendAsync(create);
        var created = await createRes.Content.ReadFromJsonAsync<ApiEnvelope<SanctionCreated>>();
        var sid = created!.Data!.SanctionId;

        var revoke = _fx.WithKey(new HttpRequestMessage(HttpMethod.Post, $"/api/internal/users/u2/sanctions/{sid}/revoke")
        {
            Content = JsonContent.Create(new { revokedBySub = "mod-1", revokeReason = "误操作" }),
        });
        await _fx.Client.SendAsync(revoke);

        var status = _fx.WithKey(new HttpRequestMessage(HttpMethod.Get, "/api/internal/users/u2/forum-sanction-status"));
        var r = await _fx.Client.SendAsync(status);
        var body = await r.Content.ReadFromJsonAsync<ApiEnvelope<ForumSanctionStatusDto>>();
        Assert.False(body!.Data!.IsMuted);
    }

    [Fact]
    public async Task Preset_7d_computes_expiry()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<UserSanctionService>();
        var record = await svc.CreateAsync("u3", UserSanctionTypes.Mute, "r", "mod", null, "7d");
        Assert.Equal(UserSanctionDurationPresets.Days7, record.DurationPreset);
        Assert.True(record.ExpiresAtUtc > DateTime.UtcNow.AddDays(6.9));
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
    }

    private sealed class SanctionCreated
    {
        public string SanctionId { get; set; } = "";
    }
}
