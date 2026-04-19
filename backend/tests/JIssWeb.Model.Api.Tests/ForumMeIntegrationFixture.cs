using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

public sealed class ForumMeIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_me_" + Guid.NewGuid().ToString("N");
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
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        var t = DateTime.UtcNow;
        await posts.InsertManyAsync(new[]
        {
            new ForumPostRecord
            {
                Id = "me-post-a",
                Title = "ta",
                Body = "ba",
                Excerpt = "ba",
                AuthorSubId = "user-a",
                Board = "综合",
                Tags = new List<string> { "alpha", "shared" },
                CreatedAtUtc = t.AddMinutes(-10),
            },
            new ForumPostRecord
            {
                Id = "me-post-b",
                Title = "tb",
                Body = "bb",
                Excerpt = "bb",
                AuthorSubId = "user-b",
                Board = "综合",
                Tags = new List<string> { "beta", "shared" },
                CreatedAtUtc = t.AddMinutes(-10),
            },
            new ForumPostRecord
            {
                Id = "me-post-tech",
                Title = "techunique",
                Body = "bt",
                Excerpt = "bt",
                AuthorSubId = "user-c",
                Board = "技术",
                Tags = new List<string> { "Alpha", "techtag" },
                CreatedAtUtc = t.AddMinutes(-9),
            },
        });
        await replies.InsertManyAsync(new[]
        {
            new ForumReplyRecord
            {
                Id = "me-reply-a",
                PostId = "me-post-a",
                AuthorSubId = "user-a",
                Body = "ra",
                CreatedAtUtc = t.AddMinutes(-5),
            },
            new ForumReplyRecord
            {
                Id = "me-reply-b",
                PostId = "me-post-b",
                AuthorSubId = "user-b",
                Body = "rb",
                CreatedAtUtc = t.AddMinutes(-5),
            },
        });

        var favorites = db.GetCollection<ForumPostFavoriteRecord>(ForumMongoSetup.FavoritesCollectionName);
        await favorites.InsertOneAsync(new ForumPostFavoriteRecord
        {
            Id = "fav-seed-user-a-post-a",
            PostId = "me-post-a",
            UserSubId = "user-a",
            CreatedAtUtc = t.AddMinutes(-2),
        });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }
}

[CollectionDefinition("ForumMe")]
public class ForumMeCollection : ICollectionFixture<ForumMeIntegrationFixture>
{
}
