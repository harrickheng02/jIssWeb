using JIssWeb.Accounting.Api.Mongo;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Accounting.Api.Tests;

public sealed class AccountingIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public string ConnectionString { get; private set; } = "";
    public string DatabaseName { get; private set; } = "";

    public Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        ConnectionString = _mongoRunner.ConnectionString;
        DatabaseName = "accounting_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(ConnectionString);
        AccountingMongoSetup.EnsureIndexes(client.GetDatabase(DatabaseName));
        return Task.CompletedTask;
    }

    public IMongoClient GetClient() => new MongoClient(ConnectionString);

    public IMongoDatabase GetDatabase() => GetClient().GetDatabase(DatabaseName);

    public async Task ClearAsync()
    {
        var db = GetDatabase();
        var names = new[]
        {
            AccountingMongoSetup.LedgerCollectionName,
            AccountingMongoSetup.TransactionsCollectionName,
            AccountingMongoSetup.ConsumedEventsCollectionName,
            AccountingMongoSetup.ConsumerStateCollectionName,
            AccountingMongoSetup.DecayRunsCollectionName,
            AccountingMongoSetup.MetricRunsCollectionName,
            AccountingMongoSetup.AiqiResultsCollectionName,
            AccountingMongoSetup.NetflowResultsCollectionName,
            AccountingMongoSetup.SkillInventoryCollectionName,
            "arena_points_events",
        };
        foreach (var name in names)
            await db.GetCollection<BsonDocument>(name).DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
    }

    public Task DisposeAsync()
    {
        _mongoRunner?.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("Accounting")]
public class AccountingCollection : ICollectionFixture<AccountingIntegrationFixture>
{
}
