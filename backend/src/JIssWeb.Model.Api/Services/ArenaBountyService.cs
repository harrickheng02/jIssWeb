using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 悬赏累积与图灵奖章事件（#34）。所有写入落在四张独立集合 + forum_announcements，
/// SHALL NOT 扩展 agent_personas / arena_points_events / ForumAnnouncementRecord 的字段。
///
/// 关键约束：
/// - 悬赏值由 ArenaBountyCalculator 纯函数 Set 覆盖写，绝不 ×2 累加（补跑 12 周会放大 4096 倍）；
/// - 按账号 × weekKey 用 arena_bounty_runs 原子领取，11000 静默跳过；
/// - 一次性事件用「survivedWeeks ≥ MedalWeeks 且 MedalAwardedAtUtc == null」下界判定，
///   并以 MedalAwardedWeekKey == weekKey 作为公告幂等锚点，使被 Skipped 的周可自愈；
/// - 公告不点名、零字段扩展、零门禁字段、写入即可见、不设 linkUrl；
/// - Enabled=false 时全部方法零写入，积分回落恰好 50。
/// </summary>
public sealed class ArenaBountyService : IArenaBountyService
{
    private readonly IMongoCollection<ArenaBountyStateRecord> _states;
    private readonly IMongoCollection<ArenaBountyRunRecord> _runs;
    private readonly IMongoCollection<ArenaBountyMedalRecord> _medals;
    private readonly IMongoCollection<ArenaHunterBadgeRecord> _badges;
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<ForumAnnouncementRecord> _announcements;
    private readonly ArenaBountyOptions _options;
    private readonly ILogger<ArenaBountyService> _logger;

    public ArenaBountyService(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ArenaBountyOptions> bountyOptions,
        ILogger<ArenaBountyService> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _states = db.GetCollection<ArenaBountyStateRecord>(ArenaMongoSetup.BountyStatesCollectionName);
        _runs = db.GetCollection<ArenaBountyRunRecord>(ArenaMongoSetup.BountyRunsCollectionName);
        _medals = db.GetCollection<ArenaBountyMedalRecord>(ArenaMongoSetup.BountyMedalsCollectionName);
        _badges = db.GetCollection<ArenaHunterBadgeRecord>(ArenaMongoSetup.HunterBadgesCollectionName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _announcements = db.GetCollection<ForumAnnouncementRecord>(ForumMongoSetup.AnnouncementsCollectionName);
        _options = bountyOptions.Value;
        _logger = logger;
    }

    public int MaxPayoutPerWeek => _options.MaxPayoutPerWeek;

    public async Task<int> ComputeWeekAsync(string weekKey, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return 0;

        var personas = await _personas
            .Find(Builders<AgentPersonaRecord>.Filter.Eq(p => p.State, AgentPersonaState.Active))
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var persona in personas)
        {
            if (string.IsNullOrWhiteSpace(persona.AgentUserId))
                continue;

            var run = new ArenaBountyRunRecord
            {
                Id = persona.AgentUserId + ":" + weekKey,
                AgentUserId = persona.AgentUserId,
                WeekKey = weekKey,
                ComputedAtUtc = DateTime.UtcNow,
            };
            try
            {
                await _runs.InsertOneAsync(run, cancellationToken: cancellationToken);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
            {
                // 同一账号同一周已计算过：静默跳过，保持 Set 幂等。
                continue;
            }

            var computation = ArenaBountyCalculator.Compute(persona.CreatedAtUtc, weekKey, _options);
            if (!computation.Applicable)
            {
                _logger.LogInformation(
                    "Arena bounty skipped for {Agent}: missing or invalid CreatedAtUtc / weekKey {WeekKey}.",
                    persona.AgentUserId, weekKey);
                continue;
            }
            if (computation.SurvivedWeeks < 1)
                continue; // 存活不足 7 天：不写悬赏状态、不发公告。

            var existing = await _states
                .Find(Builders<ArenaBountyStateRecord>.Filter.Eq(s => s.AgentUserId, persona.AgentUserId))
                .FirstOrDefaultAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var isLegendary = computation.SurvivedWeeks >= _options.MedalWeeks;
            var medalWeek = existing?.MedalAwardedWeekKey;
            var medalAt = existing?.MedalAwardedAtUtc;
            if (isLegendary && medalAt is null)
            {
                // 下界 + 未发放标记：即使「恰好第 4 周」被 Skipped，后续补跑也能恰好发放一次。
                medalWeek = weekKey;
                medalAt = now;
            }

            var survivorAt = existing?.SurvivorMarkedAtUtc;
            if (computation.SurvivedWeeks >= _options.SurvivorWeeks && survivorAt is null)
                survivorAt = now; // 纯数据标记：不触发揭露、不写经验集合、不改 persona.State。

            var state = new ArenaBountyStateRecord
            {
                Id = existing?.Id ?? persona.AgentUserId,
                AgentUserId = persona.AgentUserId,
                SurvivedWeeks = computation.SurvivedWeeks,
                CurrentBounty = computation.Bounty,
                BountyCap = _options.CapPoints,
                IsLegendaryPrey = isLegendary,
                MedalAwardedWeekKey = medalWeek,
                MedalAwardedAtUtc = medalAt,
                SurvivorMarkedAtUtc = survivorAt,
                LastComputedWeekKey = weekKey,
                UpdatedAtUtc = now,
            };
            await _states.ReplaceOneAsync(
                Builders<ArenaBountyStateRecord>.Filter.Eq(s => s.AgentUserId, persona.AgentUserId),
                state,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
            processed++;
        }

        await PublishMedalAnnouncementAsync(weekKey, cancellationToken);
        return processed;
    }

    public async Task<int> GetBountyAsync(string targetUserId, string weekKey, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return 50;

        var state = await _states
            .Find(Builders<ArenaBountyStateRecord>.Filter.Eq(s => s.AgentUserId, targetUserId))
            .FirstOrDefaultAsync(cancellationToken);
        if (state is null || state.CurrentBounty <= 0 || state.LastComputedWeekKey != weekKey)
            return 50;

        return state.CurrentBounty;
    }

    public async Task RecordHunterBadgeAsync(
        string userId,
        string targetUserId,
        string weekKey,
        int bountyPoints,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var state = await _states
            .Find(Builders<ArenaBountyStateRecord>.Filter.Eq(s => s.AgentUserId, targetUserId))
            .FirstOrDefaultAsync(cancellationToken);
        if (state is null || !state.IsLegendaryPrey)
            return; // 普通目标不授予徽章。

        var badge = new ArenaHunterBadgeRecord
        {
            Id = userId, // UserId 唯一：终身恰好一条永久成就。
            BadgeId = "turing-hunter",
            UserId = userId,
            TargetWeekKey = weekKey,
            BountyPoints = bountyPoints,
            AwardedAtUtc = DateTime.UtcNow,
        };
        try
        {
            await _badges.InsertOneAsync(badge, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // 幂等：重放结算不重复授予。
        }
    }

    /// <summary>
    /// 不点名聚合公告：只含聚合信息，固定 Id 幂等、写入即可见、零门禁字段、不设 linkUrl。
    /// 触发条件锚定 MedalAwardedWeekKey == weekKey（首次达标周），而非「恰好第 N 周」，保证可自愈且不重复。
    /// </summary>
    private async Task PublishMedalAnnouncementAsync(string weekKey, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.AnnouncementEnabled)
            return;

        var newlyAwarded = await _states
            .Find(Builders<ArenaBountyStateRecord>.Filter.Eq(s => s.MedalAwardedWeekKey, weekKey))
            .ToListAsync(cancellationToken);
        if (newlyAwarded.Count == 0)
            return;

        const string idPrefix = "arena-bounty-medal-";
        var id = idPrefix + weekKey;
        var maxBounty = newlyAwarded.Max(s => s.CurrentBounty);
        var medal = new ArenaBountyMedalRecord
        {
            Id = id,
            WeekKey = weekKey,
            MaxBounty = maxBounty,
            LegendaryCount = newlyAwarded.Count,
            CreatedAtUtc = DateTime.UtcNow,
        };
        try
        {
            await _medals.InsertOneAsync(medal, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            return; // 该周奖章事件已发布。
        }

        var announcement = new ForumAnnouncementRecord
        {
            Id = id,
            Title = "图灵奖章事件：传说级猎物出现",
            Summary = $"本周出现传说级猎物，当前最高悬赏 {maxBounty} 分。",
            LinkUrl = null, // /arena/hall-of-turing 属 #37，路由尚不存在，避免死链。
            PublishedAtUtc = DateTime.UtcNow,
            Pinned = false,
        };
        try
        {
            await _announcements.InsertOneAsync(announcement, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // 固定 Id 幂等。
        }
    }
}
