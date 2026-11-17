using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mongo2Go;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>可控的内容分类器：返回预设判定、抛异常或超时。</summary>
public sealed class StubContentModerator : IContentModerator
{
    public ModerationVerdict Verdict { get; set; } = ModerationVerdict.Ok;
    public Exception? Throw { get; set; }
    public int? DelayMs { get; set; }
    public int Calls { get; private set; }

    public async Task<ModerationVerdict> ClassifyAsync(string? title, string body, CancellationToken ct = default)
    {
        Calls++;
        if (DelayMs is int ms) await Task.Delay(ms, ct);
        if (Throw is not null) throw Throw;
        return Verdict;
    }
}

public sealed class ContentModerationFixture : IAsyncLifetime
{
    private MongoDbRunner? _runner;

    public string DatabaseName { get; } = "model_moderation_" + Guid.NewGuid().ToString("N");
    public IMongoClient Client { get; private set; } = null!;
    public StubContentModerator Moderator { get; } = new();

    public IMongoDatabase Db() => Client.GetDatabase(DatabaseName);

    public Task InitializeAsync()
    {
        _runner = MongoDbRunner.Start();
        Client = new MongoClient(_runner.ConnectionString);
        ForumMongoSetup.EnsureModerationIndexes(Db());
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _runner?.Dispose();
        return Task.CompletedTask;
    }

    public async Task ClearAsync()
    {
        await Db().GetCollection<ForumModerationQueueRecord>(ForumMongoSetup.ModerationQueueCollectionName).DeleteManyAsync(FilterDefinition<ForumModerationQueueRecord>.Empty);
        await Db().GetCollection<ForumContentHashRecord>(ForumMongoSetup.ContentHashesCollectionName).DeleteManyAsync(FilterDefinition<ForumContentHashRecord>.Empty);
        await Db().GetCollection<ForumModerationDailyCallRecord>(ForumMongoSetup.ModerationDailyCallsCollectionName).DeleteManyAsync(FilterDefinition<ForumModerationDailyCallRecord>.Empty);
        Moderator.Verdict = ModerationVerdict.Ok;
        Moderator.Throw = null;
        Moderator.DelayMs = null;
    }

    public IMongoCollection<ForumModerationDailyCallRecord> DailyCalls()
        => Db().GetCollection<ForumModerationDailyCallRecord>(ForumMongoSetup.ModerationDailyCallsCollectionName);

    /// <summary>预置当日已消耗的调用次数。</summary>
    public Task SeedDailyCallsAsync(int count)
        => DailyCalls().InsertOneAsync(new ForumModerationDailyCallRecord
        {
            Day = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Count = count,
        });

    public ContentModerationGate CreateGate(ForumContentModerationOptions? options = null)
    {
        var mongo = new OptionsStub<MongoSettings>(new MongoSettings { ConnectionString = _runner!.ConnectionString, DatabaseName = DatabaseName });
        var opts = options ?? new ForumContentModerationOptions { Enabled = true };
        return new ContentModerationGate(
            Client, mongo, Moderator,
            new OptionsStub<ForumContentModerationOptions>(opts),
            NullLogger<ContentModerationGate>.Instance);
    }

    public IMongoCollection<ForumModerationQueueRecord> Queue()
        => Db().GetCollection<ForumModerationQueueRecord>(ForumMongoSetup.ModerationQueueCollectionName);

    public static ModerationRequest Request(string id = "p1", string body = "这是一段普通内容")
        => new("post", id, id, "general", "author-1", "标题", body);
}
