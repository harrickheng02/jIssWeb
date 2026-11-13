using JIssWeb.Accounting.Api.Models;
using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Common;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Accounting.Api.Services;

/// <summary>
/// 积分账本消费者（后台服务）。从 #33 不可变事件源持续消费，维护每用户余额与只追加流水。
/// - 幂等：arena_points_consumed_events 的 eventId 唯一索引保证重放/崩溃续跑不重复计。
/// - 负余额下限 0：余额更新用 max(0, balance+delta) 原子管线。
/// - 免死护盾：处理 miss 事件时若用户持有未核销 death_shield，则 waive -5 并核销一次。
/// - 隐私：流水不写 eventId / targetUserId。
/// </summary>
public sealed class ArenaPointsConsumerService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IMongoDatabase _db;
    private readonly ILogger<ArenaPointsConsumerService> _logger;
    private readonly IMongoCollection<ArenaSourceEventRecord> _events;
    private readonly IMongoCollection<UserPointsLedgerRecord> _ledger;
    private readonly IMongoCollection<UserPointsTransactionRecord> _transactions;
    private readonly IMongoCollection<ArenaPointsConsumedEventRecord> _consumed;
    private readonly IMongoCollection<ArenaPointsConsumerStateRecord> _state;
    private readonly IMongoCollection<UserSkillInventoryRecord> _inventory;

    public ArenaPointsConsumerService(IMongoDatabase db, ILogger<ArenaPointsConsumerService> logger)
    {
        _db = db;
        _logger = logger;
        // 事件源集合名与 #33 冻结契约一致（arena_points_events）。
        _events = db.GetCollection<ArenaSourceEventRecord>("arena_points_events");
        _ledger = db.GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        _transactions = db.GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        _consumed = db.GetCollection<ArenaPointsConsumedEventRecord>(AccountingMongoSetup.ConsumedEventsCollectionName);
        _state = db.GetCollection<ArenaPointsConsumerStateRecord>(AccountingMongoSetup.ConsumerStateCollectionName);
        _inventory = db.GetCollection<UserSkillInventoryRecord>(AccountingMongoSetup.SkillInventoryCollectionName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动补跑：处理停机期间积压事件。
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>
    /// 单轮消费：从游标之后读取事件批次，幂等应用，推进游标。
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        var checkpoint = await _state.FindOneAndUpdateAsync(
            Builders<ArenaPointsConsumerStateRecord>.Filter.Eq(x => x.Id, "singleton"),
            Builders<ArenaPointsConsumerStateRecord>.Update
                .Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
            new FindOneAndUpdateOptions<ArenaPointsConsumerStateRecord> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            ct);

        var lastId = checkpoint?.LastProcessedId ?? "";
        var filter = string.IsNullOrEmpty(lastId)
            ? Builders<ArenaSourceEventRecord>.Filter.Empty
            : Builders<ArenaSourceEventRecord>.Filter.Gt(x => x.Id, lastId);
        var batch = await _events.Find(filter).Sort(Builders<ArenaSourceEventRecord>.Sort.Ascending(x => x.Id))
            .Limit(200).ToListAsync(ct);

        foreach (var ev in batch)
        {
            // 幂等护盾：eventId 唯一索引，重放/续跑重复事件会被 11000 静默跳过。
            try
            {
                await _consumed.InsertOneAsync(new ArenaPointsConsumedEventRecord { EventId = ev.EventId }, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                continue; // 已处理，跳过
            }

            await ApplyEventAsync(ev, ct);
        }

        if (batch.Count > 0)
        {
            var newestId = batch[batch.Count - 1].Id;
            await _state.UpdateOneAsync(
                Builders<ArenaPointsConsumerStateRecord>.Filter.Eq(x => x.Id, "singleton"),
                Builders<ArenaPointsConsumerStateRecord>.Update.Set(x => x.LastProcessedId, newestId),
                cancellationToken: ct);
        }
    }

    private async Task ApplyEventAsync(ArenaSourceEventRecord ev, CancellationToken ct)
    {
        // 免死护盾 waive（仅 miss 触发）。
        if (ev.Type == ArenaSourceEventType.Miss)
        {
            var shielded = await TryConsumeShieldAsync(ev.UserId, ct);
            if (shielded)
            {
                _logger.LogInformation("Death shield waived miss penalty for user {User} event {Event}", ev.UserId, ev.EventId);
                return; // 不扣分、不写流水
            }
        }

        await ApplyBalanceAsync(ev, ct);
    }

    private async Task ApplyBalanceAsync(ArenaSourceEventRecord ev, CancellationToken ct)
    {
        var old = await _ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, ev.UserId))
            .FirstOrDefaultAsync(ct);
        var oldBalance = old?.Balance ?? 0;

        // 原子更新：max(0, balance + delta)，upsert。
        var pipeline = new UpdateDefinitionBuilder<UserPointsLedgerRecord>().Pipeline(new[]
        {
            new BsonDocument("$set", new BsonDocument("Balance",
                new BsonDocument("$max", new BsonArray
                {
                    0,
                    new BsonDocument("$add", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$Balance", 0 }), ev.Delta }),
                }))),
            new BsonDocument("$set", new BsonDocument("UpdatedAtUtc", DateTime.UtcNow)),
        });
        var updated = await _ledger.FindOneAndUpdateAsync(
            Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, ev.UserId),
            pipeline,
            new FindOneAndUpdateOptions<UserPointsLedgerRecord> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            ct);

        var actualDelta = updated!.Balance - oldBalance;
        if (actualDelta == 0) return; // 钳到 0 且本就是 0（如 miss 但余额已 0 且 shield 未命中仍 -5→0 时 actualDelta 可能 0 仅当 oldBalance 已 0）

        await _transactions.InsertOneAsync(new UserPointsTransactionRecord
        {
            TxId = Guid.NewGuid().ToString("N"),
            UserId = ev.UserId,
            Type = ev.Type,
            Delta = actualDelta,
            BalanceAfter = updated.Balance,
            OccurredAtUtc = ev.OccurredAtUtc,
        }, cancellationToken: ct);
    }

    private async Task<bool> TryConsumeShieldAsync(string userId, CancellationToken ct)
    {
        var f = Builders<UserSkillInventoryRecord>.Filter;
        var shield = await _inventory.Find(
                f.Eq(x => x.UserId, userId)
                & f.Eq(x => x.SkillId, DetectiveSkillCatalog.DeathShield)
                & f.Eq(x => x.Consumed, false))
            .FirstOrDefaultAsync(ct);
        if (shield is null) return false;

        var res = await _inventory.UpdateOneAsync(
            Builders<UserSkillInventoryRecord>.Filter.Eq(x => x.Id, shield.Id)
                & Builders<UserSkillInventoryRecord>.Filter.Eq(x => x.Consumed, false),
            Builders<UserSkillInventoryRecord>.Update.Set(x => x.Consumed, true),
            cancellationToken: ct);
        return res.ModifiedCount > 0;
    }
}
