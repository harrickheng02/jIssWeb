using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

public class ArenaAccusationService : IArenaAccusationService
{
    private readonly IMongoCollection<ArenaAccusationDedupeRecord> _dedupe;
    private readonly IMongoCollection<ArenaAccusationRecord> _accusations;
    private readonly IMongoCollection<ArenaTargetTallyRecord> _tallies;
    private readonly int _threshold;
    private readonly ILogger<ArenaAccusationService> _logger;

    public ArenaAccusationService(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ArenaRevealOptions> arenaOptions,
        ILogger<ArenaAccusationService> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _dedupe = db.GetCollection<ArenaAccusationDedupeRecord>(ArenaMongoSetup.DedupeCollectionName);
        _accusations = db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);
        _tallies = db.GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName);
        _threshold = arenaOptions.Value.Threshold;
        _logger = logger;
    }

    public async Task AccuseAsync(string accuserId, string targetUserId, CancellationToken cancellationToken = default)
    {
        // 零身份判定：本路径不调用 User.Api，不读取/推断 target 的 accountType。
        var weekKey = ArenaWeekKeys.For(DateTimeOffset.UtcNow);

        // 1) 去重原子插入；7 天内重复遇 11000 静默跳过（验收①）。
        var dedupeRec = new ArenaAccusationDedupeRecord
        {
            AccuserId = accuserId,
            TargetUserId = targetUserId,
        };
        try
        {
            await _dedupe.InsertOneAsync(dedupeRec, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // 重复指认：静默忽略，不写任何业务记录、不抛、不泄露分支。
            return;
        }

        // 2) 业务记录：pending，不设 7 天 TTL（D3）。
        var accusation = new ArenaAccusationRecord
        {
            WeekKey = weekKey,
            AccuserId = accuserId,
            TargetUserId = targetUserId,
            Status = ArenaAccusationStatus.Pending,
        };
        await _accusations.InsertOneAsync(accusation, cancellationToken: cancellationToken);

        // 3) 阈值计数：$inc CAS（对所有目标累加，不查 accountType）。
        var filter = Builders<ArenaTargetTallyRecord>.Filter
            .Eq(x => x.WeekKey, weekKey)
            & Builders<ArenaTargetTallyRecord>.Filter.Eq(x => x.TargetUserId, targetUserId);
        var updated = await _tallies.FindOneAndUpdateAsync(
            filter,
            Builders<ArenaTargetTallyRecord>.Update
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.WeekKey, weekKey)
                .SetOnInsert(x => x.TargetUserId, targetUserId)
                .SetOnInsert(x => x.Status, ArenaTallyStatus.Active)
                .Inc(x => x.Count, 1)
                .Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
            new FindOneAndUpdateOptions<ArenaTargetTallyRecord>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After,
            },
            cancellationToken: cancellationToken);

        // 仅当达到阈值且仍为 active 时升为 threshold_reached；不判 agent、不置 pending_reveal（D4）。
        if (updated.Count >= _threshold && updated.Status == ArenaTallyStatus.Active)
        {
            await _tallies.UpdateOneAsync(
                Builders<ArenaTargetTallyRecord>.Filter.Eq(x => x.Id, updated.Id)
                & Builders<ArenaTargetTallyRecord>.Filter.Eq(x => x.Status, ArenaTallyStatus.Active),
                Builders<ArenaTargetTallyRecord>.Update.Set(x => x.Status, ArenaTallyStatus.ThresholdReached),
                cancellationToken: cancellationToken);
        }
    }
}
