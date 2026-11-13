using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Accounting.Api.Services;

/// <summary>
/// 月度积分衰减（后台服务）。每月 1 日对所有用户余额执行 -10%（floor 到整数），记录衰减流水。
/// 按 yyyy-MM 幂等；启动补跑停机期间缺失的月份（不补跑未来月、不回溯功能上线前的月份）。
/// </summary>
public sealed class ArenaPointsDecayService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IMongoDatabase _db;
    private readonly ILogger<ArenaPointsDecayService> _logger;
    private readonly IMongoCollection<UserPointsLedgerRecord> _ledger;
    private readonly IMongoCollection<UserPointsTransactionRecord> _transactions;
    private readonly IMongoCollection<ArenaDecayRunRecord> _runs;

    public ArenaPointsDecayService(IMongoDatabase db, ILogger<ArenaPointsDecayService> logger)
    {
        _db = db;
        _logger = logger;
        _ledger = db.GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        _transactions = db.GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        _runs = db.GetCollection<ArenaDecayRunRecord>(AccountingMongoSetup.DecayRunsCollectionName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunPendingMonthsAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPendingMonthsAsync(stoppingToken);
        }
    }

    /// <summary>
    /// 运行所有待处理的月份（从最近一次运行的下月或当月起，至当前月）。
    /// </summary>
    public async Task RunPendingMonthsAsync(CancellationToken ct = default)
    {
        var current = YearMonthString(DateTime.UtcNow);
        var latest = await LatestRunMonthAsync(ct);
        var start = latest is null ? current : AddYearMonth(latest, 1);
        var ym = start;
        while (string.Compare(ym, current, StringComparison.Ordinal) <= 0)
        {
            await RunMonthAsync(ym, ct);
            ym = AddYearMonth(ym, 1);
        }
    }

    public async Task RunMonthAsync(string yearMonth, CancellationToken ct)
    {
        // 幂等认领：yyyy-MM 唯一；已存在则跳过（11000）。
        try
        {
            await _runs.InsertOneAsync(new ArenaDecayRunRecord { YearMonth = yearMonth }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return;
        }

        var users = await _ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Gt(x => x.Balance, 0))
            .ToListAsync(ct);
        foreach (var u in users)
        {
            var newBalance = (int)Math.Floor(u.Balance * 0.9);
            if (newBalance >= u.Balance) continue; // 理论上 balance>0 时必减小
            var delta = newBalance - u.Balance;
            await _ledger.UpdateOneAsync(
                Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.Id, u.Id),
                Builders<UserPointsLedgerRecord>.Update
                    .Set(x => x.Balance, newBalance).Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: ct);
            await _transactions.InsertOneAsync(new UserPointsTransactionRecord
            {
                TxId = Guid.NewGuid().ToString("N"),
                UserId = u.UserId,
                Type = "monthly_decay",
                Delta = delta,
                BalanceAfter = newBalance,
                OccurredAtUtc = DateTime.UtcNow,
            }, cancellationToken: ct);
        }

        _logger.LogInformation("Monthly decay applied for {Month}: {Count} users", yearMonth, users.Count);
    }

    private async Task<string?> LatestRunMonthAsync(CancellationToken ct)
    {
        var last = await _runs.Find(Builders<ArenaDecayRunRecord>.Filter.Empty)
            .Sort(Builders<ArenaDecayRunRecord>.Sort.Descending(x => x.YearMonth))
            .FirstOrDefaultAsync(ct);
        return last?.YearMonth;
    }

    public static string YearMonthString(DateTime dt) => $"{dt.Year:D4}-{dt.Month:D2}";

    public static string AddYearMonth(string ym, int months)
    {
        var (y, m) = ParseYearMonth(ym);
        var total = y * 12 + (m - 1) + months;
        return $"{total / 12:D4}-{total % 12 + 1:D2}";
    }

    private static (int Y, int M) ParseYearMonth(string ym)
    {
        var parts = ym.Split('-');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
