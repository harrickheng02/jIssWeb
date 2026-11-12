using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

public sealed class ForumAntiSpamIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public TestUserSanctionClient Sanctions { get; } = new();
    public string DatabaseName { get; private set; } = "";

    public WebApplicationFactory<Program> CreateFactory(Action<IWebHostBuilder>? configure = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner!.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
            b.UseSetting("Forum:RateLimit:UseRedis", "false");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
                services.RemoveAll(typeof(IUserSanctionClient));
                services.AddSingleton<IUserSanctionClient>(Sanctions);
            });
            configure?.Invoke(b);
        });
    }

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_antispam_" + Guid.NewGuid().ToString("N");
        Factory = CreateFactory();
        Client = Factory.CreateClient();
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var posts = mongo.GetDatabase(DatabaseName).GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = "aspam-post-1",
            Title = "测试帖",
            Body = "body",
            Excerpt = "body",
            AuthorSubId = "user-author",
            Board = "综合",
            Tags = new List<string>(),
            CreatedAtUtc = DateTime.UtcNow,
        });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }
}

[CollectionDefinition("ForumAntiSpam")]
public class ForumAntiSpamCollection : ICollectionFixture<ForumAntiSpamIntegrationFixture>
{
}
