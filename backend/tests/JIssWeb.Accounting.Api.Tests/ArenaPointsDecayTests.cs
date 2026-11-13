using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Models;
using JIssWeb.Accounting.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Accounting.Api.Tests;

[Collection("Accounting")]
public class ArenaPointsDecayTests(AccountingIntegrationFixture fx)
{
    private ArenaPointsDecayService Create() => new(fx.GetDatabase(), NullLogger<ArenaPointsDecayService>.Instance);

    private async Task<int> BalanceOfAsync(string userId)
    {
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        var doc = await ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, userId)).FirstOrDefaultAsync();
        return doc?.Balance ?? 0;
    }

    private async Task<int> DecayTxCountAsync(string userId)
    {
        var tx = fx.GetDatabase().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        var tf = Builders<UserPointsTransactionRecord>.Filter;
        return (await tx.Find(tf.Eq(x => x.UserId, userId) & tf.Eq(x => x.Type, "monthly_decay")).ToListAsync()).Count;
    }

    [Fact]
    public async Task RunMonthAsync_applies_minus_10_percent()
    {
        await fx.ClearAsync();
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = "u1", Balance = 100 });

        await Create().RunMonthAsync("2026-11", default);

        Assert.Equal(90, await BalanceOfAsync("u1"));
        Assert.Equal(1, await DecayTxCountAsync("u1"));
    }

    [Fact]
    public async Task RunMonthAsync_is_idempotent_per_month()
    {
        await fx.ClearAsync();
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = "u2", Balance = 100 });

        await Create().RunMonthAsync("2026-11", default);
        await Create().RunMonthAsync("2026-11", default); // 重跑同月

        Assert.Equal(90, await BalanceOfAsync("u2"));
        Assert.Equal(1, await DecayTxCountAsync("u2"));
    }

    [Fact]
    public async Task RunPendingMonthsAsync_runs_current_month_when_none_done()
    {
        await fx.ClearAsync();
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = "u3", Balance = 100 });

        await Create().RunPendingMonthsAsync(default);

        var expected = (int)Math.Floor(100 * 0.9);
        Assert.Equal(expected, await BalanceOfAsync("u3"));
    }

    [Fact]
    public async Task RunPendingMonthsAsync_skips_already_run_current_month()
    {
        await fx.ClearAsync();
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = "u4", Balance = 100 });

        // 预置「当前月已跑」记录。
        var runs = fx.GetDatabase().GetCollection<ArenaDecayRunRecord>(AccountingMongoSetup.DecayRunsCollectionName);
        await runs.InsertOneAsync(new ArenaDecayRunRecord { YearMonth = ArenaPointsDecayService.YearMonthString(DateTime.UtcNow) });

        await Create().RunPendingMonthsAsync(default);

        Assert.Equal(100, await BalanceOfAsync("u4")); // 未衰减
        Assert.Equal(0, await DecayTxCountAsync("u4"));
    }

    [Theory]
    [InlineData("2026-11", 1, "2026-12")]
    [InlineData("2026-12", 1, "2027-01")]
    [InlineData("2026-01", -1, "2025-12")]
    public void AddYearMonth_math(string ym, int months, string expected)
        => Assert.Equal(expected, ArenaPointsDecayService.AddYearMonth(ym, months));

    [Fact]
    public async Task Two_consecutive_months_compound()
    {
        await fx.ClearAsync();
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = "u5", Balance = 100 });

        await Create().RunMonthAsync("2026-10", default);
        await Create().RunMonthAsync("2026-11", default);

        Assert.Equal(81, await BalanceOfAsync("u5")); // floor(100*0.9)=90 → floor(90*0.9)=81
    }
}
