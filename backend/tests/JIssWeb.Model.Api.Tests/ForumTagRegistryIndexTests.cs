using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Mongo2Go;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

public sealed class ForumTagRegistryIndexTests : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;
    private IMongoCollection<ForumTagRecord>? _tags;

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        var client = new MongoClient(_mongoRunner.ConnectionString);
        var db = client.GetDatabase("tag_index_" + Guid.NewGuid().ToString("N"));

        lock (typeof(ForumTagRegistryIndexTests))
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(ForumTagRecord)))
                BsonClassMap.RegisterClassMap<ForumTagRecord>(cm => cm.AutoMap());
        }

        var tags = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
        var slugIndex = Builders<ForumTagRecord>.IndexKeys.Ascending(x => x.Slug);
        await tags.Indexes.CreateOneAsync(
            new CreateIndexModel<ForumTagRecord>(slugIndex, new CreateIndexOptions { Unique = true, Name = "uniq_slug" }));
        _tags = tags;
    }

    public Task DisposeAsync()
    {
        _mongoRunner?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SlugUniqueIndex_SecondInsertSameSlug_ThrowsDuplicateKey()
    {
        var now = DateTime.UtcNow;
        await _tags!.InsertOneAsync(MakeRecord("idx-a", "重复", "重复", now));

        var ex = await Assert.ThrowsAsync<MongoWriteException>(() =>
            _tags.InsertOneAsync(MakeRecord("idx-b", "重复", "重复", now)));

        Assert.Equal(ServerErrorCategory.DuplicateKey, ex.WriteError.Category);
    }

    [Fact]
    public async Task SlugUniqueIndex_ConcurrentInserts_OnlyOneSucceeds()
    {
        var now = DateTime.UtcNow;
        var tasks = Enumerable.Range(0, 5)
            .Select(i => _tags!.InsertOneAsync(MakeRecord($"concurrent-{i}", "并发", "并发", now)))
            .ToList();

        int succeeded = 0, duplicates = 0;
        await Task.WhenAll(tasks.Select(async t =>
        {
            try
            {
                await t;
                Interlocked.Increment(ref succeeded);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                Interlocked.Increment(ref duplicates);
            }
        }));

        Assert.Equal(1, succeeded);
        Assert.Equal(4, duplicates);
        var count = await _tags!.CountDocumentsAsync(
            Builders<ForumTagRecord>.Filter.Eq(x => x.Slug, "并发"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SlugUniqueIndex_DifferentSlugs_AllSucceed()
    {
        var now = DateTime.UtcNow;
        await _tags!.InsertOneAsync(MakeRecord("diff-a", "标签A", "标签a", now));
        await _tags!.InsertOneAsync(MakeRecord("diff-b", "标签B", "标签b", now));
        await _tags!.InsertOneAsync(MakeRecord("diff-c", "标签C", "标签c", now));

        var count = await _tags!.CountDocumentsAsync(
            Builders<ForumTagRecord>.Filter.In(x => x.Id, new[] { "diff-a", "diff-b", "diff-c" }));
        Assert.Equal(3, count);
    }

    private static ForumTagRecord MakeRecord(string id, string name, string slug, DateTime now) =>
        new()
        {
            Id = id,
            Name = name,
            Slug = slug,
            Status = ForumTagStatuses.Active,
            UseCount = 0,
            CreatedAtUtc = now,
            CreatedBySub = "test",
        };
}
