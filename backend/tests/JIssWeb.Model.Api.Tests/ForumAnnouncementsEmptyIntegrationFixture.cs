using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

/// <summary>Model API with empty MongoDB (no announcement rows) for empty-list tests.</summary>
public sealed class ForumAnnouncementsEmptyIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_ann_empty_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
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
}

[CollectionDefinition("ForumAnnouncementsEmpty")]
public class ForumAnnouncementsEmptyCollection : ICollectionFixture<ForumAnnouncementsEmptyIntegrationFixture>
{
}
