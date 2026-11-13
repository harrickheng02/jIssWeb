using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Mongo;

/// <summary>
/// 图灵场竞技域集合与索引注册（D3 生命周期解耦）。
/// - arena_accusation_dedupe：唯一索引 {accuserId,targetUserId} + TTL 7 天，专责去重。
/// - arena_accusations：不设 7 天 TTL，结算后归档清理（避免漏结算）。
/// - arena_target_tallies：{weekKey,targetUserId} 唯一，$inc CAS。
/// - arena_reveal_jobs：weekKey 唯一，批次状态机。
/// - arena_points_events：(weekId,userId,targetUserId,type) 唯一，只追加。
/// </summary>
public static class ArenaMongoSetup
{
    public const string DedupeCollectionName = "arena_accusation_dedupe";
    public const string AccusationsCollectionName = "arena_accusations";
    public const string TalliesCollectionName = "arena_target_tallies";
    public const string RevealJobsCollectionName = "arena_reveal_jobs";
    public const string SettlementPlansCollectionName = "arena_settlement_plans";
    public const string PointsEventsCollectionName = "arena_points_events";

    public static void EnsureIndexes(IMongoDatabase db)
    {
        var dedupe = db.GetCollection<ArenaAccusationDedupeRecord>(DedupeCollectionName);
        // 唯一复合索引：7 天内同一 (accuserId,targetUserId) 只能存在一条（去重正确性）。
        dedupe.Indexes.CreateOne(new CreateIndexModel<ArenaAccusationDedupeRecord>(
            Builders<ArenaAccusationDedupeRecord>.IndexKeys
                .Ascending(x => x.AccuserId).Ascending(x => x.TargetUserId),
            new CreateIndexOptions<ArenaAccusationDedupeRecord>
            {
                Unique = true,
                Name = "uniq_accuser_target",
            }));
        // TTL 必须是单字段索引（MongoDB 不支持复合索引带 TTL）：7 天到期由 CreatedAtUtc 承载，
        // 与唯一复合索引解耦；到期删除后允许第 8 天重新指认（验收①）。
        dedupe.Indexes.CreateOne(new CreateIndexModel<ArenaAccusationDedupeRecord>(
            Builders<ArenaAccusationDedupeRecord>.IndexKeys.Ascending(x => x.CreatedAtUtc),
            new CreateIndexOptions<ArenaAccusationDedupeRecord>
            {
                Name = "dedupe_ttl_7d",
                ExpireAfter = TimeSpan.FromDays(7),
            }));

        var accusations = db.GetCollection<ArenaAccusationRecord>(AccusationsCollectionName);
        // 注意：arena_accusations 不设 7 天 TTL（D3 必修）。仅建查询索引与归档扫描索引。
        accusations.Indexes.CreateOne(new CreateIndexModel<ArenaAccusationRecord>(
            Builders<ArenaAccusationRecord>.IndexKeys
                .Ascending(x => x.WeekKey).Ascending(x => x.TargetUserId).Ascending(x => x.AccuserId),
            new CreateIndexOptions { Name = "week_target_accuser" }));
        accusations.Indexes.CreateOne(new CreateIndexModel<ArenaAccusationRecord>(
            Builders<ArenaAccusationRecord>.IndexKeys.Ascending(x => x.SettledAtUtc),
            new CreateIndexOptions { Name = "settled_at_purge" }));

        var tallies = db.GetCollection<ArenaTargetTallyRecord>(TalliesCollectionName);
        tallies.Indexes.CreateOne(new CreateIndexModel<ArenaTargetTallyRecord>(
            Builders<ArenaTargetTallyRecord>.IndexKeys
                .Ascending(x => x.WeekKey).Ascending(x => x.TargetUserId),
            new CreateIndexOptions { Unique = true, Name = "uniq_week_target" }));

        var jobs = db.GetCollection<ArenaRevealJobRecord>(RevealJobsCollectionName);
        jobs.Indexes.CreateOne(new CreateIndexModel<ArenaRevealJobRecord>(
            Builders<ArenaRevealJobRecord>.IndexKeys.Ascending(x => x.WeekKey),
            new CreateIndexOptions { Unique = true, Name = "uniq_week" }));
        jobs.Indexes.CreateOne(new CreateIndexModel<ArenaRevealJobRecord>(
            Builders<ArenaRevealJobRecord>.IndexKeys.Ascending(x => x.State),
            new CreateIndexOptions { Name = "state" }));

        var plans = db.GetCollection<ArenaSettlementPlanRecord>(SettlementPlansCollectionName);
        // 结算计划 weekKey 唯一：prepare 幂等（已存在则读取既有计划，不重复判定）。
        plans.Indexes.CreateOne(new CreateIndexModel<ArenaSettlementPlanRecord>(
            Builders<ArenaSettlementPlanRecord>.IndexKeys.Ascending(x => x.WeekKey),
            new CreateIndexOptions { Unique = true, Name = "uniq_week" }));

        var events = db.GetCollection<ArenaPointsEventRecord>(PointsEventsCollectionName);
        // eventId 由 (weekId,userId,targetUserId,type) 确定性派生；此四字段唯一索引是重放幂等真护盾。
        events.Indexes.CreateOne(new CreateIndexModel<ArenaPointsEventRecord>(
            Builders<ArenaPointsEventRecord>.IndexKeys
                .Ascending(x => x.WeekId).Ascending(x => x.UserId)
                .Ascending(x => x.TargetUserId).Ascending(x => x.Type),
            new CreateIndexOptions
            {
                Unique = true,
                Name = "uniq_week_user_target_type",
            }));
        events.Indexes.CreateOne(new CreateIndexModel<ArenaPointsEventRecord>(
            Builders<ArenaPointsEventRecord>.IndexKeys.Ascending(x => x.EventId),
            new CreateIndexOptions { Unique = true, Name = "uniq_eventId" }));
    }
}
