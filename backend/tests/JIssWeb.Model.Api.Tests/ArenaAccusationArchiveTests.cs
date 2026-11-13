using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 图灵场指认记录归档测试（G8/D3）：仅清理「已结算且 settledAtUtc ≤ cutoff」的记录，
/// 未结算（settledAtUtc 为空）与未超保留期的记录必须保留；不早于结算完成（用例 22 P0）。
/// </summary>
[Collection("ArenaReveal")]
public class ArenaAccusationArchiveTests
{
    private readonly ArenaRevealIntegrationFixture _fx;

    public ArenaAccusationArchiveTests(ArenaRevealIntegrationFixture fx) => _fx = fx;

    private IMongoCollection<ArenaAccusationRecord> Accusations()
        => _fx.GetClient().GetDatabase(_fx.DatabaseName)
            .GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);

    private ArenaAccusationArchiveService CreateService()
        => new ArenaAccusationArchiveService(
            _fx.GetClient(),
            new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName }),
            NullLogger<ArenaAccusationArchiveService>.Instance);

    [Fact]
    public async Task PurgeAsync_仅删超期已结算_保留未结算与未超期()
    {
        await _fx.ClearAsync();
        var now = DateTime.UtcNow;
        var col = Accusations();

        // (a) 已结算且 30 天前 → 应删。
        col.InsertOne(new ArenaAccusationRecord { WeekKey = "w1", AccuserId = "u1", TargetUserId = "t1", Status = "settled", SettledAtUtc = now.AddDays(-30) });
        // (b) 已结算且 5 天前（未超 21 天保留期）→ 应保留。
        col.InsertOne(new ArenaAccusationRecord { WeekKey = "w2", AccuserId = "u2", TargetUserId = "t2", Status = "settled", SettledAtUtc = now.AddDays(-5) });
        // (c) 未结算（settledAtUtc 空）→ 必须保留（不早于结算完成）。
        col.InsertOne(new ArenaAccusationRecord { WeekKey = "w3", AccuserId = "u3", TargetUserId = "t3", Status = "pending", SettledAtUtc = null });

        // cutoff = now - 21 天：仅 (a) 命中。
        var deleted = await CreateService().PurgeAsync(now.AddDays(-21));
        Assert.Equal(1, deleted);

        var remaining = await col.CountDocumentsAsync(FilterDefinition<ArenaAccusationRecord>.Empty);
        Assert.Equal(2, remaining); // (b) 与 (c) 保留。
        Assert.Equal(1, await col.CountDocumentsAsync(Builders<ArenaAccusationRecord>.Filter.Eq(x => x.TargetUserId, "t3")));
    }
}
