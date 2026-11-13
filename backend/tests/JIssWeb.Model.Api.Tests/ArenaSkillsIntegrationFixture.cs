using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 图灵场技能效果集成 fixture（#35）。提供隔离 Mongo 库，供行为扫描 / 公开质询服务与控制器测试使用。
/// 清空 user_skill_inventory / forum_posts / forum_replies / arena_inquiry_tasks。
/// </summary>
public sealed class ArenaSkillsIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public string ConnectionString { get; private set; } = "";
    public string DatabaseName { get; private set; } = "";

    public Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        ConnectionString = _mongoRunner.ConnectionString;
        DatabaseName = "model_arena_skills_" + Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    public IMongoClient GetClient() => new MongoClient(ConnectionString);

    public IMongoDatabase GetDatabase() => GetClient().GetDatabase(DatabaseName);

    public ArenaSkillEffectsService CreateService() => new(GetDatabase());

    public async Task ClearAsync()
    {
        var db = GetDatabase();
        foreach (var name in new[] { "user_skill_inventory", ForumMongoSetup.PostsCollectionName, ForumMongoSetup.RepliesCollectionName, "arena_inquiry_tasks" })
            await db.GetCollection<BsonDocument>(name).DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
    }

    public Task DisposeAsync()
    {
        _mongoRunner?.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("ArenaSkills")]
public class ArenaSkillsCollection : ICollectionFixture<ArenaSkillsIntegrationFixture>
{
}
