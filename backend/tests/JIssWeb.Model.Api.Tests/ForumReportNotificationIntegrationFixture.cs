using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
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

public sealed class ForumReportNotificationIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_rn_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
            b.UseSetting("Forum:Moderation:Moderators:0:Sub", "user-mod");
            b.UseSetting("Forum:Moderation:Moderators:0:BoardIds:0", "general");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
            });
        });
        Client = Factory.CreateClient();
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        await posts.InsertManyAsync(new[]
        {
            new ForumPostRecord
            {
                Id = "rn-post-1",
                Title = "举报通知测试帖",
                Body = "body",
                Excerpt = "body",
                AuthorSubId = "user-author",
                Board = "综合",
                Tags = new List<string>(),
                CreatedAtUtc = DateTime.UtcNow,
            },
        });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }
}

[CollectionDefinition("ForumReportNotification")]
public class ForumReportNotificationCollection : ICollectionFixture<ForumReportNotificationIntegrationFixture>
{
}
