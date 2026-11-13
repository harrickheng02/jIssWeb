using System.Globalization;
using System.Security.Claims;
using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Controllers;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// #34 悬赏能力 Mongo2Go 自包含集成测试：A / B / C / D 类红线。
/// 复用 ArenaRevealIntegrationFixture（ClearAsync 已覆盖新增四集合 + 公告集合），
/// 测试类前缀 ArenaBounty* 自动落入 CI 的 Arena|Agent 过滤器。
/// </summary>
[Collection("ArenaReveal")]
public class ArenaBountyIntegrationTests
{
    private const string WeekKey = "2026-07-06"; // 周一（Asia/Shanghai）

    private readonly ArenaRevealIntegrationFixture _fx;

    public ArenaBountyIntegrationTests(ArenaRevealIntegrationFixture fx) => _fx = fx;

    private static DateTime WeekEndUtc => new DateTimeOffset(
        DateTime.ParseExact(WeekKey, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(7),
        TimeSpan.FromHours(8)).ToUniversalTime().UtcDateTime;

    private static string WeekAt(int offsetWeeks) => DateTime
        .ParseExact(WeekKey, "yyyy-MM-dd", CultureInfo.InvariantCulture)
        .AddDays(7 * offsetWeeks).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private IMongoDatabase Db() => _fx.GetClient().GetDatabase(_fx.DatabaseName);

    private IMongoCollection<ArenaBountyStateRecord> States()
        => Db().GetCollection<ArenaBountyStateRecord>(ArenaMongoSetup.BountyStatesCollectionName);

    private IMongoCollection<BsonDocument> AnnouncementsRaw()
        => Db().GetCollection<BsonDocument>(ForumMongoSetup.AnnouncementsCollectionName);

    private (ArenaBountyService Bounty, ArenaRevealService Reveal) CreateServices(ArenaBountyOptions? bountyOptions = null)
    {
        var client = _fx.GetClient();
        IOptions<MongoSettings> opts = new OptionsStub<MongoSettings>(
            new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<ArenaBountyOptions> bountyOpts = new OptionsStub<ArenaBountyOptions>(
            bountyOptions ?? new ArenaBountyOptions());
        var bounty = new ArenaBountyService(client, opts, bountyOpts, NullLogger<ArenaBountyService>.Instance);
        IOptions<ArenaRevealOptions> arenaOpts = new OptionsStub<ArenaRevealOptions>(new ArenaRevealOptions());
        var reveal = new ArenaRevealService(
            client,
            opts,
            arenaOpts,
            new StubConfessionGenerator("我是自白正文。"),
            new PassBlockedWordFilter(),
            NullLogger<ArenaRevealService>.Instance,
            bounty);
        return (bounty, reveal);
    }

    private static ArenaBountyOptions Enabled() => new() { Enabled = true };

    private void SeedPersona(string id, DateTime createdAtUtc, int survivalDays = 0, string? nickname = null)
    {
        Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).InsertOne(new AgentPersonaRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            PersonaId = "p-" + id,
            AgentUserId = id,
            Nickname = nickname ?? "潜伏者-" + id,
            State = AgentPersonaState.Active,
            CreatedAtUtc = createdAtUtc,
            SurvivalDays = survivalDays,
        });
    }

    private void SeedTally(string week, string target, int count = 3)
    {
        Db().GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName).InsertOne(new ArenaTargetTallyRecord
        {
            WeekKey = week,
            TargetUserId = target,
            Count = count,
            Status = "threshold_reached",
            UpdatedAtUtc = DateTime.UtcNow,
        });
    }

    private void SeedAccusations(string week, string target, params string[] accusers)
    {
        var col = Db().GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);
        foreach (var a in accusers)
            col.InsertOne(new ArenaAccusationRecord
            {
                WeekKey = week,
                AccuserId = a,
                TargetUserId = target,
                Status = "pending",
                CreatedAtUtc = DateTime.UtcNow,
            });
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 100)]
    [InlineData(4, 400)]
    [InlineData(9, 400)]
    public async Task ComputeWeek_悬赏序列按CreatedAtUtc推导且上限收敛(int weeks, int expected)
    {
        await _fx.ClearAsync();
        SeedPersona("prey", WeekEndUtc.AddDays(-7 * weeks));

        var (bounty, _) = CreateServices(Enabled());
        Assert.Equal(1, await bounty.ComputeWeekAsync(WeekKey));

        var state = await States().Find(FilterDefinition<ArenaBountyStateRecord>.Empty).FirstAsync();
        Assert.Equal(weeks, state.SurvivedWeeks);
        Assert.Equal(expected, state.CurrentBounty);
        Assert.Equal(WeekKey, state.LastComputedWeekKey);
        Assert.True(state.CurrentBounty <= state.BountyCap);
    }

    [Fact]
    public async Task ComputeWeek_补跑12个不同weekKey不重复翻倍且公告恰好一条()
    {
        await _fx.ClearAsync();
        SeedPersona("prey", WeekEndUtc.AddDays(-7));
        var (bounty, _) = CreateServices(Enabled());

        for (var i = 0; i < 12; i++)
            await bounty.ComputeWeekAsync(WeekAt(i));

        var states = await States().Find(FilterDefinition<ArenaBountyStateRecord>.Empty).ToListAsync();
        var state = Assert.Single(states);
        Assert.Equal(WeekAt(11), state.LastComputedWeekKey);
        Assert.Equal(12, state.SurvivedWeeks);
        Assert.Equal(400, state.CurrentBounty); // 绝不 ×2 累加放大到 4096 倍。

        var runs = await Db().GetCollection<ArenaBountyRunRecord>(ArenaMongoSetup.BountyRunsCollectionName)
            .CountDocumentsAsync(FilterDefinition<ArenaBountyRunRecord>.Empty);
        Assert.Equal(12, runs); // 每周各自独立领取。

        var announcements = await AnnouncementsRaw().CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        Assert.Equal(1, announcements); // 奖章一次性事件全网恰好发一次。
    }

    [Fact]
    public async Task ComputeWeek_同一weekKey重跑三次保持幂等()
    {
        await _fx.ClearAsync();
        SeedPersona("prey", WeekEndUtc.AddDays(-28));
        var (bounty, _) = CreateServices(Enabled());

        await bounty.ComputeWeekAsync(WeekKey);
        await bounty.ComputeWeekAsync(WeekKey);
        await bounty.ComputeWeekAsync(WeekKey);

        Assert.Equal(1, await States().CountDocumentsAsync(FilterDefinition<ArenaBountyStateRecord>.Empty));
        Assert.Equal(1, await Db().GetCollection<ArenaBountyRunRecord>(ArenaMongoSetup.BountyRunsCollectionName)
            .CountDocumentsAsync(FilterDefinition<ArenaBountyRunRecord>.Empty));
        Assert.Equal(1, await AnnouncementsRaw().CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task ComputeWeek_灰度关闭零写入且积分回落恰好50()
    {
        await _fx.ClearAsync();
        SeedPersona("prey", WeekEndUtc.AddDays(-28));
        var (bounty, _) = CreateServices(); // 默认 Enabled=false

        Assert.Equal(0, await bounty.ComputeWeekAsync(WeekKey));
        Assert.Equal(0, await States().CountDocumentsAsync(FilterDefinition<ArenaBountyStateRecord>.Empty));
        Assert.Equal(0, await AnnouncementsRaw().CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(50, await bounty.GetBountyAsync("prey", WeekKey));
    }

    [Fact]
    public async Task ComputeWeek_手工SurvivalDays不影响且缺CreatedAtUtc跳过不抛异常()
    {
        await _fx.ClearAsync();
        SeedPersona("manual", WeekEndUtc.AddDays(-14), survivalDays: 999);
        SeedPersona("legacy", default);
        var (bounty, _) = CreateServices(Enabled());

        await bounty.ComputeWeekAsync(WeekKey);

        var manual = await States().Find(s => s.AgentUserId == "manual").FirstAsync();
        Assert.Equal(2, manual.SurvivedWeeks);
        Assert.Equal(100, manual.CurrentBounty);
        Assert.Empty(await States().Find(s => s.AgentUserId == "legacy").ToListAsync());
    }

    [Fact]
    public async Task ComputeWeek_in_progress周被跳过后用下界自愈补发奖章公告()
    {
        await _fx.ClearAsync();
        // 「恰好第 4 周」被 Skipped：直接以第 6 周作为首次计算周，存活周数已越过 MedalWeeks。
        SeedPersona("prey", WeekEndUtc.AddDays(-7 * 4));
        var (bounty, _) = CreateServices(Enabled());
        var skippedWeek = WeekAt(2); // 第 6 周

        await bounty.ComputeWeekAsync(skippedWeek);

        var state = await States().Find(FilterDefinition<ArenaBountyStateRecord>.Empty).FirstAsync();
        Assert.True(state.SurvivedWeeks >= 4);
        Assert.NotNull(state.MedalAwardedAtUtc);
        Assert.Equal(skippedWeek, state.MedalAwardedWeekKey);

        var ann = await AnnouncementsRaw().Find(FilterDefinition<BsonDocument>.Empty).FirstAsync();
        Assert.Equal("arena-bounty-medal-" + skippedWeek, ann["_id"].AsString);
        // 下一周再跑不重复发放（下界条件 + MedalAwardedAtUtc 已置位）。
        await bounty.ComputeWeekAsync(WeekAt(3));
        Assert.Equal(1, await AnnouncementsRaw().CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task RevealWeek_传说级猎物每位指认者各获全额悬赏且事件契约零扩展()
    {
        await _fx.ClearAsync();
        const string target = "legend-prey";
        SeedPersona(target, WeekEndUtc.AddDays(-28), nickname: "绝密昵称-ZZZ");
        SeedTally(WeekKey, target);
        SeedAccusations(WeekKey, target, "h1", "h2", "h3");
        var (bounty, reveal) = CreateServices(Enabled());
        await bounty.ComputeWeekAsync(WeekKey);

        Assert.Equal(RevealOutcome.Processed, await reveal.RevealWeekAsync(WeekKey));

        var events = await Db().GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .Find(e => e.WeekId == WeekKey).ToListAsync();
        Assert.Equal(3, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal("accusation_hit", e.Type); // 零新增 type
            Assert.Equal(400, e.Delta);            // 每人全额，不分摊
            Assert.True(e.NotifyEligible);
        });

        // 零新增字段：生产方字段集合与 #33 完全一致（无 bountyPoints 等）。
        var raw = await Db().GetCollection<BsonDocument>(ArenaMongoSetup.PointsEventsCollectionName)
            .Find(FilterDefinition<BsonDocument>.Empty).FirstAsync();
        foreach (var forbidden in new[] { "bountyPoints", "BountyPoints", "bounty", "Bounty" })
            Assert.False(raw.Contains(forbidden), $"事件文档不应含新字段 {forbidden}");

        // 徽章：每位成功指认者恰好一条（UserId 唯一）。
        Assert.Equal(3, await Db().GetCollection<ArenaHunterBadgeRecord>(ArenaMongoSetup.HunterBadgesCollectionName)
            .CountDocumentsAsync(FilterDefinition<ArenaHunterBadgeRecord>.Empty));
    }

    [Fact]
    public async Task RevealWeek_灰度关闭时积分恰好回落50且无徽章()
    {
        await _fx.ClearAsync();
        const string target = "plain-prey";
        SeedPersona(target, WeekEndUtc.AddDays(-28));
        SeedTally(WeekKey, target);
        SeedAccusations(WeekKey, target, "h1", "h2");
        var (_, reveal) = CreateServices(); // Enabled=false

        Assert.Equal(RevealOutcome.Processed, await reveal.RevealWeekAsync(WeekKey));

        var events = await Db().GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .Find(e => e.WeekId == WeekKey).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(50, e.Delta));
        Assert.Empty(await Db().GetCollection<ArenaHunterBadgeRecord>(ArenaMongoSetup.HunterBadgesCollectionName)
            .Find(FilterDefinition<ArenaHunterBadgeRecord>.Empty).ToListAsync());
    }

    [Fact]
    public async Task ComputeWeek_聚合公告零账号标识零门禁字段且不设linkUrl()
    {
        await _fx.ClearAsync();
        SeedPersona("secret-prey", WeekEndUtc.AddDays(-28), nickname: "绝不应出现的昵称");
        var (bounty, _) = CreateServices(Enabled());
        await bounty.ComputeWeekAsync(WeekKey);

        var ann = await AnnouncementsRaw().Find(FilterDefinition<BsonDocument>.Empty).FirstAsync();
        var text = ann["Title"].AsString + (ann["Summary"].IsBsonNull ? "" : ann["Summary"].AsString);
        Assert.DoesNotContain("secret-prey", text);
        Assert.DoesNotContain("绝不应出现的昵称", text);
        Assert.DoesNotContain("28", text); // 不含存活天数与账号的对应关系
        Assert.Contains("400", text);      // 只含聚合悬赏信息

        foreach (var forbidden in new[] { "BatchId", "ReleasedAtUtc", "AnnouncedAtUtc", "IsPublic" })
            Assert.False(ann.Contains(forbidden), $"公告不应含门禁字段 {forbidden}");
        Assert.True(ann["LinkUrl"].IsBsonNull); // 不设 linkUrl（#37 路由尚不存在）
    }

    [Fact]
    public async Task ComputeWeek_幸存者只打标记不揭露不写经验()
    {
        await _fx.ClearAsync();
        SeedPersona("survivor", WeekEndUtc.AddDays(-7 * 9));
        var (bounty, _) = CreateServices(new ArenaBountyOptions { Enabled = true, SurvivorWeeks = 8 });
        await bounty.ComputeWeekAsync(WeekKey);

        var state = await States().Find(FilterDefinition<ArenaBountyStateRecord>.Empty).FirstAsync();
        Assert.NotNull(state.SurvivorMarkedAtUtc);

        var persona = await Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .Find(p => p.AgentUserId == "survivor").FirstAsync();
        Assert.Equal(AgentPersonaState.Active, persona.State); // 不揭露、不改状态
        Assert.Empty(await Db().GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName)
            .Find(FilterDefinition<AgentExperienceRecord>.Empty).ToListAsync());
    }

    private HunterBadgeController CreateBadgeController(ClaimsPrincipal user)
    {
        IOptions<MongoSettings> opts = new OptionsStub<MongoSettings>(
            new MongoSettings { DatabaseName = _fx.DatabaseName });
        return new HunterBadgeController(_fx.GetClient(), opts)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };
    }

    [Fact]
    public async Task HunterBadgeMe_只返回本人徽章且零目标账号标识()
    {
        await _fx.ClearAsync();
        var col = Db().GetCollection<ArenaHunterBadgeRecord>(ArenaMongoSetup.HunterBadgesCollectionName);
        col.InsertOne(new ArenaHunterBadgeRecord
        {
            Id = "h1", UserId = "h1", BadgeId = "turing-hunter",
            TargetWeekKey = WeekKey, BountyPoints = 400, AwardedAtUtc = DateTime.UtcNow,
        });
        col.InsertOne(new ArenaHunterBadgeRecord
        {
            Id = "h2", UserId = "h2", BadgeId = "turing-hunter",
            TargetWeekKey = WeekKey, BountyPoints = 999, AwardedAtUtc = DateTime.UtcNow,
        });

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "h1") }, "test"));
        var action = await CreateBadgeController(user).Me(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<HunterBadgeItemDto?>>(ok.Value);
        Assert.True(payload.Success);
        var dto = payload.Data;
        Assert.NotNull(dto);
        Assert.Equal("turing-hunter", dto!.BadgeId);
        Assert.Equal(400, dto.BountyPoints); // 不是 h2 的 999，证明按 sub 隔离
        Assert.Equal(WeekKey, dto.TargetWeekKey);

        // DTO 不含任何被揭露目标账号标识。
        Assert.DoesNotContain(typeof(HunterBadgeItemDto).GetProperties(),
            p => p.Name.Contains("TargetUser", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HunterBadgeMe_缺sub时返回401且无徽章数据()
    {
        await _fx.ClearAsync();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var action = await CreateBadgeController(anonymous).Me(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<HunterBadgeItemDto?>>(unauthorized.Value);
        Assert.False(payload.Success);
        Assert.Null(payload.Data);
    }

    [Fact]
    public async Task ComputeWeek_已揭露账号停止累积()
    {
        await _fx.ClearAsync();
        SeedPersona("active-prey", WeekEndUtc.AddDays(-14));
        Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).InsertOne(new AgentPersonaRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            PersonaId = "p-eliminated",
            AgentUserId = "eliminated-prey",
            Nickname = "已揭露",
            State = AgentPersonaState.Eliminated,
            CreatedAtUtc = WeekEndUtc.AddDays(-28),
        });
        var (bounty, _) = CreateServices(Enabled());

        await bounty.ComputeWeekAsync(WeekKey);

        Assert.Equal(1, await States().CountDocumentsAsync(FilterDefinition<ArenaBountyStateRecord>.Empty));
        Assert.NotNull(await States().Find(s => s.AgentUserId == "active-prey").FirstOrDefaultAsync());
        Assert.Null(await States().Find(s => s.AgentUserId == "eliminated-prey").FirstOrDefaultAsync());
    }

    [Fact]
    public async Task RevealWeek_悬赏计算失败不阻断结算且积分回落50()
    {
        await _fx.ClearAsync();
        const string target = "fallback-prey";
        SeedPersona(target, WeekEndUtc.AddDays(-28));
        SeedTally(WeekKey, target);
        SeedAccusations(WeekKey, target, "f1", "f2");

        IOptions<MongoSettings> opts = new OptionsStub<MongoSettings>(
            new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<ArenaRevealOptions> arenaOpts = new OptionsStub<ArenaRevealOptions>(new ArenaRevealOptions());
        var reveal = new ArenaRevealService(
            _fx.GetClient(), opts, arenaOpts,
            new StubConfessionGenerator("我是自白正文。"),
            new PassBlockedWordFilter(),
            NullLogger<ArenaRevealService>.Instance,
            new ThrowingBountyService());

        Assert.Equal(RevealOutcome.Processed, await reveal.RevealWeekAsync(WeekKey));
        var events = await Db().GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .Find(e => e.WeekId == WeekKey).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(50, e.Delta));
    }

    [Fact]
    public async Task RevealWeek_单周投放超上限只告警不削减任何指认者金额()
    {
        await _fx.ClearAsync();
        const string target = "cap-prey";
        SeedPersona(target, WeekEndUtc.AddDays(-28));
        SeedTally(WeekKey, target);
        SeedAccusations(WeekKey, target, "c1", "c2", "c3");
        var (bounty, reveal) = CreateServices(
            new ArenaBountyOptions { Enabled = true, MaxPayoutPerWeek = 100 }); // 远低于 3 × 400
        await bounty.ComputeWeekAsync(WeekKey);

        Assert.Equal(RevealOutcome.Processed, await reveal.RevealWeekAsync(WeekKey));

        var events = await Db().GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .Find(e => e.WeekId == WeekKey).ToListAsync();
        Assert.Equal(3, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(400, e.Delta);      // 不削减、不分摊、不截断名单
            Assert.Equal("accusation_hit", e.Type);
        });
    }
}

/// <summary>模拟悬赏计算抛异常，验证结算批次不被阻断且积分回落基础分。</summary>
internal sealed class ThrowingBountyService : IArenaBountyService
{
    public int MaxPayoutPerWeek => 0;

    public Task<int> ComputeWeekAsync(string weekKey, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("simulated bounty computation failure");

    public Task<int> GetBountyAsync(string targetUserId, string weekKey, CancellationToken cancellationToken = default)
        => Task.FromResult(50);

    public Task RecordHunterBadgeAsync(
        string userId, string targetUserId, string weekKey, int bountyPoints,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
