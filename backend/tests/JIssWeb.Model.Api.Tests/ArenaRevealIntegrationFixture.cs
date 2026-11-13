using JIssWeb.Model.Api.Mongo;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

public sealed class ArenaRevealIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public string ConnectionString { get; private set; } = "";
    public string DatabaseName { get; private set; } = "";

    public Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        ConnectionString = _mongoRunner.ConnectionString;
        DatabaseName = "model_arena_reveal_" + Guid.NewGuid().ToString("N");

        // 与生产 EnsureIndexes 等价，隔离运行；建立 reveal_jobs.weekKey 唯一索引（原子领取护盾）。
        var client = new MongoClient(ConnectionString);
        ArenaMongoSetup.EnsureIndexes(client.GetDatabase(DatabaseName));
        return Task.CompletedTask;
    }

    public IMongoClient GetClient() => new MongoClient(ConnectionString);

    /// <summary>
    /// 清空竞技场全部域集合（含通知/帖子/人格集合），使每个测试自包含、幂等于任意运行顺序与多次运行。
    /// </summary>
    public async Task ClearAsync()
    {
        var db = GetClient().GetDatabase(DatabaseName);
        var names = new[]
        {
            ArenaMongoSetup.DedupeCollectionName,
            ArenaMongoSetup.AccusationsCollectionName,
            ArenaMongoSetup.TalliesCollectionName,
            ArenaMongoSetup.RevealJobsCollectionName,
            ArenaMongoSetup.SettlementPlansCollectionName,
            ArenaMongoSetup.PointsEventsCollectionName,
            ForumMongoSetup.NotificationsCollectionName,
            ForumMongoSetup.PostsCollectionName,
            AgentMongoSetup.PersonasCollectionName,
        };
        foreach (var name in names)
            await db.GetCollection<BsonDocument>(name)
                .DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
    }

    public Task DisposeAsync()
    {
        _mongoRunner?.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("ArenaReveal")]
public class ArenaRevealCollection : ICollectionFixture<ArenaRevealIntegrationFixture>
{
}
