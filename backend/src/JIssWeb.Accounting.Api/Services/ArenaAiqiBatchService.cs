using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Models;
using JIssWeb.Common;
using MongoDB.Driver;

namespace JIssWeb.Accounting.Api.Services;

/// <summary>
/// 图灵场 AIQI 指标与净流量周报批处理（#35，落 Accounting.Api）。
/// - 检测成功率 = 本周 hit / (hit+miss)；活跃用户 = 本周 distinct 指认者。
/// - AIQI = (1 - 检测成功率) × min(指认次数 / (活跃×0.5), 1)；猎手修正系数 = 指认次数 / (活跃×0.5)，&lt; 0.3 标「样本不足」。
/// - 净流量 = 本周 transactions Σdelta；&gt;0 通胀预警；&lt; -15% 存量 → 向低余额用户发热身包（写 transaction + 原子加余额）。
/// - 按 weekKey（周一日期）幂等：arena_metric_runs 唯一索引保证整周只算一次。
/// 只读 #33 事件源与账本/流水，不修改 #33 契约。
/// </summary>
public sealed class ArenaAiqiBatchService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int LowBalanceThreshold = 10;
    private const int WarmupGrant = 15;
    private const string WarmupType = "warmup_pack";
    private const double SampleSufficientCoef = 0.3;

    private readonly IMongoDatabase _db;
    private readonly ILogger<ArenaAiqiBatchService> _logger;
    private readonly IMongoCollection<ArenaSourceEventRecord> _events;
    private readonly IMongoCollection<UserPointsTransactionRecord> _transactions;
    private readonly IMongoCollection<UserPointsLedgerRecord> _ledger;
    private readonly IMongoCollection<ArenaAiqiResultRecord> _aiqiResults;
    private readonly IMongoCollection<ArenaNetflowResultRecord> _netflowResults;
    private readonly IMongoCollection<ArenaMetricRunRecord> _runs;

    public ArenaAiqiBatchService(IMongoDatabase db, ILogger<ArenaAiqiBatchService> logger)
    {
        _db = db;
        _logger = logger;
        _events = db.GetCollection<ArenaSourceEventRecord>("arena_points_events");
        _transactions = db.GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        _ledger = db.GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        _aiqiResults = db.GetCollection<ArenaAiqiResultRecord>(AccountingMongoSetup.AiqiResultsCollectionName);
        _netflowResults = db.GetCollection<ArenaNetflowResultRecord>(AccountingMongoSetup.NetflowResultsCollectionName);
        _runs = db.GetCollection<ArenaMetricRunRecord>(AccountingMongoSetup.MetricRunsCollectionName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunPendingAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunPendingAsync(stoppingToken);
    }

    /// <summary>补跑自最早事件周起至上周末的所有未完成周（幂等）。</summary>
    public async Task RunPendingAsync(CancellationToken ct = default)
    {
        var earliest = await _events.Find(Builders<ArenaSourceEventRecord>.Filter.Empty)
            .Sort(Builders<ArenaSourceEventRecord>.Sort.Ascending(e => e.OccurredAtUtc))
            .FirstOrDefaultAsync(ct);
        var firstWeekStart = StartOfWeek(earliest?.OccurredAtUtc ?? DateTime.UtcNow.AddDays(-7));
        var lastWeekStart = StartOfWeek(DateTime.UtcNow.AddDays(-7)); // 上周一
        for (var ws = firstWeekStart; ws <= lastWeekStart; ws = ws.AddDays(7))
        {
            await RunWeekAsync(WeekKeyOf(ws), ws, ws.AddDays(7), ct);
        }
    }

    /// <summary>计算并持久化单个自然周（[weekStart, weekEnd)）。幂等：同 weekKey 仅执行一次。</summary>
    public async Task RunWeekAsync(string weekKey, DateTime weekStart, DateTime weekEnd, CancellationToken ct = default)
    {
        try
        {
            await _runs.InsertOneAsync(new ArenaMetricRunRecord { WeekKey = weekKey }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogInformation("AIQI week {Week} already computed; skipping", weekKey);
            return;
        }

        var evF = Builders<ArenaSourceEventRecord>.Filter;
        var weekEvents = await _events.Find(
                evF.Gte(e => e.OccurredAtUtc, weekStart) & evF.Lt(e => e.OccurredAtUtc, weekEnd))
            .ToListAsync(ct);

        var hits = weekEvents.Count(e => e.Type == ArenaSourceEventType.Hit);
        var misses = weekEvents.Count(e => e.Type == ArenaSourceEventType.Miss);
        var totalAccusations = weekEvents.Count;
        var activeUsers = weekEvents.Select(e => e.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().Count();
        var (aiqi, sampleSufficient, hunterCoef) = ComputeMetrics(hits, misses, activeUsers);
        var detectionSuccessRate = totalAccusations == 0 ? 0d : (double)hits / totalAccusations;

        var txF = Builders<UserPointsTransactionRecord>.Filter;
        var weekTx = await _transactions.Find(
                txF.Gte(t => t.OccurredAtUtc, weekStart) & txF.Lt(t => t.OccurredAtUtc, weekEnd))
            .ToListAsync(ct);
        var netFlow = weekTx.Sum(t => (long)t.Delta);

        var stock = await _ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Empty)
            .Project(Builders<UserPointsLedgerRecord>.Projection.Expression(x => x.Balance))
            .ToListAsync(ct);
        var totalStock = stock.Sum(b => (long)b);

        var inflationAlert = netFlow > 0;
        var warmupIssued = false;
        if (totalStock > 0 && netFlow < -0.15 * totalStock)
        {
            warmupIssued = await IssueWarmupPacksAsync(ct);
        }

        await _aiqiResults.InsertOneAsync(new ArenaAiqiResultRecord
        {
            WeekKey = weekKey,
            Aiqi = aiqi,
            DetectionSuccessRate = detectionSuccessRate,
            TotalAccusations = totalAccusations,
            ActiveUsers = activeUsers,
            SampleSufficient = sampleSufficient,
        }, cancellationToken: ct);

        await _netflowResults.InsertOneAsync(new ArenaNetflowResultRecord
        {
            WeekKey = weekKey,
            NetFlow = (int)netFlow,
            InflationAlert = inflationAlert,
            WarmupPackIssued = warmupIssued,
        }, cancellationToken: ct);

        _logger.LogInformation(
            "AIQI week {Week}: aiqi={Aiqi:F3} rate={Rate:F3} active={Active} sampleOk={Sample} netFlow={Net} inflation={Inflation} warmup={Warmup}",
            weekKey, aiqi, detectionSuccessRate, activeUsers, sampleSufficient, netFlow, inflationAlert, warmupIssued);
    }

    /// <summary>向所有余额低于阈值的用户发放热身包（原子加余额 + 写流水），返回是否发放。</summary>
    private async Task<bool> IssueWarmupPacksAsync(CancellationToken ct)
    {
        var low = await _ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Lt(x => x.Balance, LowBalanceThreshold))
            .ToListAsync(ct);
        if (low.Count == 0) return false;

        foreach (var user in low)
        {
            var pipeline = new UpdateDefinitionBuilder<UserPointsLedgerRecord>().Pipeline(new[]
            {
                new MongoDB.Bson.BsonDocument("$set", new MongoDB.Bson.BsonDocument("Balance",
                    new MongoDB.Bson.BsonDocument("$max", new MongoDB.Bson.BsonArray
                    {
                        0,
                        new MongoDB.Bson.BsonDocument("$add", new MongoDB.Bson.BsonArray
                        {
                            new MongoDB.Bson.BsonDocument("$ifNull", new MongoDB.Bson.BsonArray { "$Balance", 0 }),
                            WarmupGrant,
                        }),
                    }))),
                new MongoDB.Bson.BsonDocument("$set", new MongoDB.Bson.BsonDocument("UpdatedAtUtc", DateTime.UtcNow)),
            });
            var updated = await _ledger.FindOneAndUpdateAsync(
                Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.Id, user.Id),
                pipeline,
                new FindOneAndUpdateOptions<UserPointsLedgerRecord> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
                cancellationToken: ct);

            var actualDelta = updated!.Balance - user.Balance;
            if (actualDelta <= 0) continue;
            await _transactions.InsertOneAsync(new UserPointsTransactionRecord
            {
                TxId = Guid.NewGuid().ToString("N"),
                UserId = user.UserId,
                Type = WarmupType,
                Delta = actualDelta,
                BalanceAfter = updated.Balance,
                OccurredAtUtc = DateTime.UtcNow,
            }, cancellationToken: ct);
        }

        return true;
    }

    /// <summary>
    /// 纯函数：由命中/未中/活跃侦探数计算 AIQI 与样本充足度。
    /// AIQI = (1 - 检测成功率) × min(指认 / (活跃×0.5), 1)；猎手参与修正系数 = 指认 / (活跃×0.5)，&lt; 0.3 视为样本不足。
    /// 注：按事件源数据（活跃≤指认），修正系数通常 ≥ 2；样本不足分支仅在活跃侦探基数远大于指认数时触发（如活跃侦探来自更广义的论坛活跃口径）。
    /// </summary>
    public static (double Aiqi, bool SampleSufficient, double CorrectionCoef) ComputeMetrics(
        int hits, int misses, int activeUsers)
    {
        var totalAccusations = hits + misses;
        var detectionSuccessRate = totalAccusations == 0 ? 0d : (double)hits / totalAccusations;
        var correctionCoef = activeUsers == 0 ? 0d : totalAccusations / (activeUsers * 0.5);
        var sampleSufficient = correctionCoef >= SampleSufficientCoef;
        var aiqi = (1 - detectionSuccessRate) * Math.Min(correctionCoef, 1d);
        return (aiqi, sampleSufficient, correctionCoef);
    }

    /// <summary>ISO 周起点（周一，UTC）。</summary>
    public static DateTime StartOfWeek(DateTime dt)
    {
        var d = dt.Date;
        var diff = (int)d.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        return d.AddDays(-diff);
    }

    public static string WeekKeyOf(DateTime weekStart) => weekStart.ToString("yyyy-MM-dd");
}
