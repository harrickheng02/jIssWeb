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

/// <summary>
/// AI 版主阶段一写入口集成测试基建：Mongo2Go + 可替换分类器。
/// 默认关闭开关，各用例按需在 <see cref="CreateFactory"/> 的 configure 中开启。
/// </summary>
public sealed class ContentModerationIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _runner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public TestUserSanctionClient Sanctions { get; } = new();
    public StubContentModerator Moderator { get; } = new();
    public string DatabaseName { get; private set; } = "";

    public WebApplicationFactory<Program> CreateFactory(Action<IWebHostBuilder>? configure = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _runner!.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
            b.UseSetting("Forum:RateLimit:UseRedis", "false");
            b.UseSetting("Forum:Moderation:Moderators:0:Sub", "mod-user");
            b.UseSetting("Forum:Moderation:Moderators:0:BoardIds:0", "general");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
                services.RemoveAll(typeof(IUserSanctionClient));
                services.AddSingleton<IUserSanctionClient>(Sanctions);
                services.RemoveAll(typeof(IContentModerator));
                services.AddSingleton<IContentModerator>(Moderator);
            });
            configure?.Invoke(b);
        });
    }

    public async Task InitializeAsync()
    {
        _runner = MongoDbRunner.Start();
        DatabaseName = "model_contentmod_" + Guid.NewGuid().ToString("N");
        Factory = CreateFactory();
        Client = Factory.CreateClient();
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        await mongo.GetDatabase(DatabaseName)
            .GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
            .InsertOneAsync(new ForumPostRecord
            {
                Id = "mod-post-1",
                Title = "测试帖",
                Body = "body",
                Excerpt = "body",
                AuthorSubId = "user-author",
                Board = "综合",
                Tags = new List<string>(),
                CreatedAtUtc = DateTime.UtcNow,
            });
    }

    public IMongoDatabase Db() => new MongoClient(_runner!.ConnectionString).GetDatabase(DatabaseName);

    public IMongoCollection<ForumPostRecord> Posts()
        => Db().GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);

    public IMongoCollection<ForumReplyRecord> Replies()
        => Db().GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);

    public IMongoCollection<ForumModerationQueueRecord> Queue()
        => Db().GetCollection<ForumModerationQueueRecord>(ForumMongoSetup.ModerationQueueCollectionName);

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _runner?.Dispose();
    }
}

[CollectionDefinition("ContentModeration")]
public class ContentModerationCollection : ICollectionFixture<ContentModerationIntegrationFixture>
{
}
