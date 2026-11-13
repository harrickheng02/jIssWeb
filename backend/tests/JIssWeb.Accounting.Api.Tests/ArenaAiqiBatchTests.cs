using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Models;
using JIssWeb.Accounting.Api.Services;
using JIssWeb.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Accounting.Api.Tests;

[Collection("Accounting")]
public class ArenaAiqiBatchTests(AccountingIntegrationFixture fx)
{
    private IMongoDatabase Db() => fx.GetDatabase();

    private async Task SeedEventAsync(string userId, string type, DateTime at, int delta = 50)
    {
        await Db().GetCollection<ArenaSourceEventRecord>("arena_points_events").InsertOneAsync(new ArenaSourceEventRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            EventId = $"{at:yyyyMMdd}|{userId}|t|{type}",
            UserId = userId,
            Type = type,
            Delta = delta,
            TargetUserId = "t_" + userId,
            WeekId = "wk",
            OccurredAtUtc = at,
            NotifyEligible = false,
        });
    }

    private async Task SeedTransactionAsync(string userId, int delta, DateTime at)
    {
        await Db().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName)
            .InsertOneAsync(new UserPointsTransactionRecord
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                TxId = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Type = "accusation_hit",
                Delta = delta,
                BalanceAfter = delta,
                OccurredAtUtc = at,
            });
    }

    private async Task SeedLedgerAsync(string userId, int balance)
    {
        await Db().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName)
            .InsertOneAsync(new UserPointsLedgerRecord { UserId = userId, Balance = balance });
    }

    [Fact]
    public async Task RunWeekAsync_computes_success_rate_and_aiqi()
    {
        await fx.ClearAsync();
        var start = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc); // 周一
        var end = start.AddDays(7);
        // u1: 2 hit, u2: 1 miss → 成功率 2/3, 活跃 2, 指认 3, hunterCoef = 3/(2*0.5)=3 → 1(clamp)
        await SeedEventAsync("u1", ArenaSourceEventType.Hit, start.AddDays(1));
        await SeedEventAsync("u1", ArenaSourceEventType.Hit, start.AddDays(2));
        await SeedEventAsync("u2", ArenaSourceEventType.Miss, start.AddDays(3), -5);

        var svc = new ArenaAiqiBatchService(Db(), NullLogger<ArenaAiqiBatchService>.Instance);
        await svc.RunWeekAsync("2026-02-02", start, end);

        var r = await Db().GetCollection<ArenaAiqiResultRecord>(AccountingMongoSetup.AiqiResultsCollectionName)
            .Find(Builders<ArenaAiqiResultRecord>.Filter.Eq(x => x.WeekKey, "2026-02-02")).FirstAsync();
        Assert.Equal(2d / 3, r.DetectionSuccessRate, 5);
        Assert.Equal(3, r.TotalAccusations);
        Assert.Equal(2, r.ActiveUsers);
        Assert.True(r.SampleSufficient);
        Assert.Equal((1 - 2d / 3) * 1d, r.Aiqi, 5);
    }

    [Fact]
    public void ComputeMetrics_flags_insufficient_sample_when_coef_below_0_3()
    {
        // 猎手参与修正系数 = 指认 / (活跃×0.5)。活跃侦探基数远大于指认数时（如广义论坛活跃口径）系数可低于 0.3。
        var (_, sufficient, coef) = ArenaAiqiBatchService.ComputeMetrics(1, 0, 10);
        Assert.True(coef < 0.3);
        Assert.False(sufficient);
    }

    [Fact]
    public void ComputeMetrics_sufficient_when_coef_at_least_0_3()
    {
        // 2 hit + 1 miss，活跃 1 → 系数 = 3/(1*0.5) = 6 ≥ 0.3，充足。
        var (aiqi, sufficient, _) = ArenaAiqiBatchService.ComputeMetrics(2, 1, 1);
        Assert.True(sufficient);
        Assert.Equal((1 - 2d / 3) * 1d, aiqi, 5);
    }

    [Fact]
    public async Task RunWeekAsync_is_idempotent_per_week()
    {
        await fx.ClearAsync();
        var start = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(7);
        await SeedEventAsync("u1", ArenaSourceEventType.Hit, start.AddDays(1));

        var svc = new ArenaAiqiBatchService(Db(), NullLogger<ArenaAiqiBatchService>.Instance);
        await svc.RunWeekAsync("2026-02-02", start, end);
        await svc.RunWeekAsync("2026-02-02", start, end); // 幂等：重复写入应被跳过

        var count = await Db().GetCollection<ArenaAiqiResultRecord>(AccountingMongoSetup.AiqiResultsCollectionName)
            .CountDocumentsAsync(Builders<ArenaAiqiResultRecord>.Filter.Eq(x => x.WeekKey, "2026-02-02"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RunWeekAsync_inflation_alert_when_netflow_positive()
    {
        await fx.ClearAsync();
        var start = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(7);
        await SeedTransactionAsync("u1", 50, start.AddDays(1));
        await SeedTransactionAsync("u2", 30, start.AddDays(2));

        var svc = new ArenaAiqiBatchService(Db(), NullLogger<ArenaAiqiBatchService>.Instance);
        await svc.RunWeekAsync("2026-02-02", start, end);

        var nf = await Db().GetCollection<ArenaNetflowResultRecord>(AccountingMongoSetup.NetflowResultsCollectionName)
            .Find(Builders<ArenaNetflowResultRecord>.Filter.Eq(x => x.WeekKey, "2026-02-02")).FirstAsync();
        Assert.Equal(80, nf.NetFlow);
        Assert.True(nf.InflationAlert);
    }

    [Fact]
    public async Task RunWeekAsync_issues_warmup_pack_when_netflow_below_15pct_stock()
    {
        await fx.ClearAsync();
        var start = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(7);
        // 存量 200（两位各 100），本周净流出 -60（>15% of 200 = -30）→ 触发热身包，发给低余额(<10)用户。
        await SeedLedgerAsync("rich", 190);
        await SeedLedgerAsync("poor", 8);
        await SeedTransactionAsync("poor", -60, start.AddDays(1));

        var svc = new ArenaAiqiBatchService(Db(), NullLogger<ArenaAiqiBatchService>.Instance);
        await svc.RunWeekAsync("2026-02-02", start, end);

        var nf = await Db().GetCollection<ArenaNetflowResultRecord>(AccountingMongoSetup.NetflowResultsCollectionName)
            .Find(Builders<ArenaNetflowResultRecord>.Filter.Eq(x => x.WeekKey, "2026-02-02")).FirstAsync();
        Assert.False(nf.InflationAlert);
        Assert.True(nf.WarmupPackIssued);

        // poor 用户 (8) +15 → 23；rich 用户不受影响。
        var poor = await Db().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName)
            .Find(Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, "poor")).FirstAsync();
        Assert.Equal(23, poor.Balance);
        var warmTx = await Db().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName)
            .Find(Builders<UserPointsTransactionRecord>.Filter.Eq(x => x.Type, "warmup_pack")).ToListAsync();
        Assert.Single(warmTx);
        Assert.Equal(15, warmTx[0].Delta);
    }

    [Fact]
    public void StartOfWeek_returns_monday()
    {
        // 2026-02-04 是周三 → 周一为 2026-02-02
        Assert.Equal(new DateTime(2026, 2, 2), ArenaAiqiBatchService.StartOfWeek(new DateTime(2026, 2, 4)));
        // 周一本身
        Assert.Equal(new DateTime(2026, 2, 2), ArenaAiqiBatchService.StartOfWeek(new DateTime(2026, 2, 2)));
    }
}
