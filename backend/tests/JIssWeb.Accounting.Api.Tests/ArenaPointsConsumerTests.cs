using JIssWeb.Accounting.Api.Models;
using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Services;
using JIssWeb.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Accounting.Api.Tests;

[Collection("Accounting")]
public class ArenaPointsConsumerTests(AccountingIntegrationFixture fx)
{
    private static readonly string Events = "arena_points_events";

    private IMongoCollection<ArenaSourceEventRecord> EventsCol()
        => fx.GetDatabase().GetCollection<ArenaSourceEventRecord>(Events);

    private ArenaPointsConsumerService Create() => new(fx.GetDatabase(), NullLogger<ArenaPointsConsumerService>.Instance);

    private async Task<ArenaSourceEventRecord> SeedEventAsync(string userId, string type, int delta, string week = "2026-W47")
    {
        var ev = new ArenaSourceEventRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            EventId = $"{week}|{userId}|target_{userId}|{type}",
            UserId = userId,
            Type = type,
            Delta = delta,
            TargetUserId = "target_" + userId,
            WeekId = week,
            OccurredAtUtc = DateTime.UtcNow,
            NotifyEligible = type == "accusation_hit",
        };
        await EventsCol().InsertOneAsync(ev);
        return ev;
    }

    private async Task<int> BalanceOfAsync(string userId)
    {
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        var doc = await ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, userId)).FirstOrDefaultAsync();
        return doc?.Balance ?? 0;
    }

    [Fact]
    public async Task Hit_event_increases_balance_by_50_and_writes_one_transaction()
    {
        await fx.ClearAsync();
        await SeedEventAsync("u1", "accusation_hit", 50);
        await Create().RunOnceAsync();

        Assert.Equal(50, await BalanceOfAsync("u1"));

        var tx = fx.GetDatabase().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        var list = await tx.Find(Builders<UserPointsTransactionRecord>.Filter.Eq(x => x.UserId, "u1")).ToListAsync();
        Assert.Single(list);
        Assert.Equal(50, list[0].Delta);
        Assert.Equal(50, list[0].BalanceAfter);
        Assert.DoesNotContain("eventId", list[0].ToString());
    }

    [Fact]
    public async Task Miss_event_decreases_balance_by_5()
    {
        await fx.ClearAsync();
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = "u2", Balance = 10 });
        await SeedEventAsync("u2", "accusation_miss", -5);
        await Create().RunOnceAsync();

        Assert.Equal(5, await BalanceOfAsync("u2"));
        var tx = fx.GetDatabase().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        var list = await tx.Find(Builders<UserPointsTransactionRecord>.Filter.Eq(x => x.UserId, "u2")).ToListAsync();
        Assert.Single(list);
        Assert.Equal(-5, list[0].Delta);
    }

    [Fact]
    public async Task Replaying_same_event_does_not_double_count()
    {
        await fx.ClearAsync();
        await SeedEventAsync("u3", "accusation_hit", 50);
        var svc = Create();
        await svc.RunOnceAsync();
        await svc.RunOnceAsync(); // 重放：事件仍在集合中，但已被消费去重

        Assert.Equal(50, await BalanceOfAsync("u3"));
        var tx = fx.GetDatabase().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        var list = await tx.Find(Builders<UserPointsTransactionRecord>.Filter.Eq(x => x.UserId, "u3")).ToListAsync();
        Assert.Single(list);
    }

    [Fact]
    public async Task Negative_balance_floor_clamps_to_zero_with_real_delta()
    {
        await fx.ClearAsync();
        // 预置余额 2，再 miss -5 → 钳到 0，流水 delta = -2。
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = "u4", Balance = 2 });

        await SeedEventAsync("u4", "accusation_miss", -5);
        await Create().RunOnceAsync();

        Assert.Equal(0, await BalanceOfAsync("u4"));
        var tx = fx.GetDatabase().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        var list = await tx.Find(Builders<UserPointsTransactionRecord>.Filter.Eq(x => x.UserId, "u4")).ToListAsync();
        Assert.Single(list);
        Assert.Equal(-2, list[0].Delta); // 真实扣减
        Assert.Equal(0, list[0].BalanceAfter);
    }

    [Fact]
    public async Task Death_shield_waives_miss_penalty_and_is_consumed_once()
    {
        await fx.ClearAsync();
        var inv = fx.GetDatabase().GetCollection<UserSkillInventoryRecord>(AccountingMongoSetup.SkillInventoryCollectionName);
        await inv.InsertOneAsync(new UserSkillInventoryRecord { UserId = "u5", SkillId = DetectiveSkillCatalog.DeathShield, Consumed = false });

        await SeedEventAsync("u5", "accusation_miss", -5);
        await Create().RunOnceAsync();

        Assert.Equal(0, await BalanceOfAsync("u5")); // 无扣分
        var tx = fx.GetDatabase().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        var list = await tx.Find(Builders<UserPointsTransactionRecord>.Filter.Eq(x => x.UserId, "u5")).ToListAsync();
        Assert.Empty(list); // 免死护盾不产生流水

        var after = await inv.Find(Builders<UserSkillInventoryRecord>.Filter.Eq(x => x.UserId, "u5")).FirstOrDefaultAsync();
        Assert.NotNull(after);
        Assert.True(after!.Consumed); // 护盾核销
    }

    [Fact]
    public async Task Death_shield_not_owned_still_applies_miss_penalty()
    {
        await fx.ClearAsync();
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = "u6", Balance = 10 });
        await SeedEventAsync("u6", "accusation_miss", -5);
        await Create().RunOnceAsync();

        Assert.Equal(5, await BalanceOfAsync("u6"));
    }
}
