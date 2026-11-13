using JIssWeb.Model.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Mongo2Go;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 阈值可配置验证（用例 5）：把 ArenaReveal:Threshold 覆盖为 2，断言阈值按配置生效。
/// </summary>
public sealed class ArenaAccusationThreshold2IntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_arena_t2_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("ArenaReveal:Threshold", "2");
        });
        Client = Factory.CreateClient();
        await Task.CompletedTask;
    }

    public IMongoDatabase GetDatabase()
    {
        using var scope = Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        return mongo.GetDatabase(DatabaseName);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
            await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }
}

[CollectionDefinition("ArenaAccusationThreshold2")]
public class ArenaAccusationThreshold2Collection : ICollectionFixture<ArenaAccusationThreshold2IntegrationFixture>
{
}
