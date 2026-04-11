using JIssWeb.Common.Options;
using JIssWeb.User.Api.Controllers;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.User.Api.Mongo;

public static class UserMongoSetup
{
    public static void EnsureIndexes(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var mongo = scope.ServiceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
        var db = client.GetDatabase(mongo.DatabaseName);

        var users = db.GetCollection<UserAccount>("users");
        var emailKeys = Builders<UserAccount>.IndexKeys.Ascending(x => x.Email);
        users.Indexes.CreateOne(new CreateIndexModel<UserAccount>(emailKeys, new CreateIndexOptions { Unique = true }));

        var sessions = db.GetCollection<RefreshSession>("refresh_sessions");
        var tokenHashKeys = Builders<RefreshSession>.IndexKeys.Ascending(x => x.TokenHash);
        sessions.Indexes.CreateOne(new CreateIndexModel<RefreshSession>(tokenHashKeys, new CreateIndexOptions { Unique = true }));
        var userIdKeys = Builders<RefreshSession>.IndexKeys.Ascending(x => x.UserId);
        sessions.Indexes.CreateOne(new CreateIndexModel<RefreshSession>(userIdKeys));
    }
}
