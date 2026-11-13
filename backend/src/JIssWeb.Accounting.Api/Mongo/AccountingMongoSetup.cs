using JIssWeb.Accounting.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Accounting.Api.Mongo;

/// <summary>
/// 图灵场积分经济集合与索引注册（#35）。所有集合落在与 Model.Api 共享的 Mongo 库内，
/// 逻辑归属按集合区分；arena_points_consumed_events 仅作内部消费去重，绝不序列化到 API。
/// </summary>
public static class AccountingMongoSetup
{
    public const string LedgerCollectionName = "user_points_ledger";
    public const string TransactionsCollectionName = "user_points_transactions";
    public const string ConsumedEventsCollectionName = "arena_points_consumed_events";
    public const string ConsumerStateCollectionName = "arena_points_consumer_state";
    public const string DecayRunsCollectionName = "arena_decay_runs";
    public const string MetricRunsCollectionName = "arena_metric_runs";
    public const string AiqiResultsCollectionName = "arena_aiqi_results";
    public const string NetflowResultsCollectionName = "arena_netflow_results";
    public const string SkillInventoryCollectionName = "user_skill_inventory";

    public static void EnsureIndexes(IMongoDatabase db)
    {
        var ledger = db.GetCollection<UserPointsLedgerRecord>(LedgerCollectionName);
        ledger.Indexes.CreateOne(new CreateIndexModel<UserPointsLedgerRecord>(
            Builders<UserPointsLedgerRecord>.IndexKeys.Ascending(x => x.UserId),
            new CreateIndexOptions { Unique = true, Name = "uniq_user" }));

        var tx = db.GetCollection<UserPointsTransactionRecord>(TransactionsCollectionName);
        tx.Indexes.CreateOne(new CreateIndexModel<UserPointsTransactionRecord>(
            Builders<UserPointsTransactionRecord>.IndexKeys
                .Ascending(x => x.UserId).Ascending(x => x.OccurredAtUtc),
            new CreateIndexOptions { Name = "user_time" }));

        var consumed = db.GetCollection<ArenaPointsConsumedEventRecord>(ConsumedEventsCollectionName);
        consumed.Indexes.CreateOne(new CreateIndexModel<ArenaPointsConsumedEventRecord>(
            Builders<ArenaPointsConsumedEventRecord>.IndexKeys.Ascending(x => x.EventId),
            new CreateIndexOptions { Unique = true, Name = "uniq_event" }));

        var decay = db.GetCollection<ArenaDecayRunRecord>(DecayRunsCollectionName);
        decay.Indexes.CreateOne(new CreateIndexModel<ArenaDecayRunRecord>(
            Builders<ArenaDecayRunRecord>.IndexKeys.Ascending(x => x.YearMonth),
            new CreateIndexOptions { Unique = true, Name = "uniq_yyyymm" }));

        var metric = db.GetCollection<ArenaMetricRunRecord>(MetricRunsCollectionName);
        metric.Indexes.CreateOne(new CreateIndexModel<ArenaMetricRunRecord>(
            Builders<ArenaMetricRunRecord>.IndexKeys.Ascending(x => x.WeekKey),
            new CreateIndexOptions { Unique = true, Name = "uniq_week" }));

        var inventory = db.GetCollection<UserSkillInventoryRecord>(SkillInventoryCollectionName);
        inventory.Indexes.CreateOne(new CreateIndexModel<UserSkillInventoryRecord>(
            Builders<UserSkillInventoryRecord>.IndexKeys
                .Ascending(x => x.UserId).Ascending(x => x.SkillId).Ascending(x => x.Consumed),
            new CreateIndexOptions { Name = "user_skill_consumed" }));
    }
}
