using JIssWeb.Common.Options;
using JIssWeb.Customer.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Customer.Api.Mongo;

public static class CustomerMongoSetup
{
    public const string CollectionName = "customers";
    public const string ProfileCollectionName = "profiles";

    public static void EnsureIndexes(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var mongo = scope.ServiceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
        var coll = client.GetDatabase(mongo.DatabaseName).GetCollection<CustomerRecord>(CollectionName);
        var ownerKeys = Builders<CustomerRecord>.IndexKeys.Ascending(x => x.OwnerUserId);
        coll.Indexes.CreateOne(new CreateIndexModel<CustomerRecord>(ownerKeys));

        var profileColl = client.GetDatabase(mongo.DatabaseName).GetCollection<ProfileRecord>(ProfileCollectionName);
        var profileOwnerKeys = Builders<ProfileRecord>.IndexKeys.Ascending(x => x.OwnerUserId);
        profileColl.Indexes.CreateOne(new CreateIndexModel<ProfileRecord>(profileOwnerKeys, new CreateIndexOptions { Unique = true }));
    }
}
