using JIssWeb.Model.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mongo2Go;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

public sealed class ArenaAccusationIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_arena_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.ConfigureTestServices(services =>
            {
                // Model.Api 在 Redis 缺省时不注册 IConnectionMultiplexer，服务按 fail-open 处理；无需 mock。
            });
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

[CollectionDefinition("ArenaAccusation")]
public class ArenaAccusationCollection : ICollectionFixture<ArenaAccusationIntegrationFixture>
{
}
