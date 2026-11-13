using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 图灵场揭露管线集成测试（G5 prepare + G6 commit）：agent / human 三分支、积分事件、+50 通知、
/// 自白帖（arena_pending → published）、AI 标记 eliminated、单 CAS 对外门禁。
/// 所有断言按 weekKey / target 范围，避免与同 Collection 其它测试累积污染。
/// </summary>
[Collection("ArenaReveal")]
public class ArenaRevealPipelineTests
{
    private readonly ArenaRevealIntegrationFixture _fx;

    public ArenaRevealPipelineTests(ArenaRevealIntegrationFixture fx) => _fx = fx;

    private ArenaRevealService CreateService(
        IArenaConfessionGenerator? confessionGenerator = null,
        IForumBlockedWordFilter? blockedWords = null)
    {
        var client = _fx.GetClient();
        IOptions<MongoSettings> opts = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });
        IOptions<ArenaRevealOptions> arenaOpts = new OptionsStub<ArenaRevealOptions>(new ArenaRevealOptions());
        return new ArenaRevealService(
            client,
            opts,
            arenaOpts,
            confessionGenerator ?? new StubConfessionGenerator("我是自白正文。"),
            blockedWords ?? new PassBlockedWordFilter(),
            NullLogger<ArenaRevealService>.Instance);
    }

    private IMongoDatabase Db() => _fx.GetClient().GetDatabase(_fx.DatabaseName);

    private void SeedPersona(string agentUserId)
    {
        Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).InsertOne(new AgentPersonaRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            PersonaId = "p-" + agentUserId,
            AgentUserId = agentUserId,
            Nickname = agentUserId,
            State = AgentPersonaState.Active,
            CreatedAtUtc = DateTime.UtcNow,
        });
    }

    private void SeedTally(string week, string target, int count, string status = "threshold_reached")
    {
        Db().GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName).InsertOne(new ArenaTargetTallyRecord
        {
            WeekKey = week,
            TargetUserId = target,
            Count = count,
            Status = status,
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

    [Fact]
    public async Task RevealWeek_agent目标_全链路结算_事件通知自白帖AI标记齐备且门禁释放()
    {
        await _fx.ClearAsync();
        const string week = "2026-07-01";
        const string target = "agent-pipeline";
        SeedPersona(target);
        SeedTally(week, target, 3);
        SeedAccusations(week, target, "a1", "a2", "a3");

        var svc = CreateService();
        var outcome = await svc.RevealWeekAsync(week);
        Assert.Equal(RevealOutcome.Processed, outcome);

        var db = Db();
        // 积分事件：3 条 hit / +50 / notifyEligible=true。
        var events = await db.GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .Find(Builders<ArenaPointsEventRecord>.Filter.Eq(e => e.WeekId, week)).ToListAsync();
        Assert.Equal(3, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal("accusation_hit", e.Type);
            Assert.Equal(50, e.Delta);
            Assert.True(e.NotifyEligible);
            Assert.Equal(target, e.TargetUserId);
        });

        // +50 通知：3 条，type=ArenaRevealHit，BatchId 置位（门禁）且 ReleasedAtUtc 已释放（completed 后）。
        var job = await db.GetCollection<ArenaRevealJobRecord>(ArenaMongoSetup.RevealJobsCollectionName)
            .Find(Builders<ArenaRevealJobRecord>.Filter.Eq(j => j.WeekKey, week)).FirstAsync();
        Assert.Equal("completed", job.State);
        var notifs = await db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName)
            .Find(Builders<InAppNotificationRecord>.Filter.Eq(n => n.BatchId, job.Id)).ToListAsync();
        Assert.Equal(3, notifs.Count);
        Assert.All(notifs, n =>
        {
            Assert.Equal(InAppNotificationTypes.ArenaRevealHit, n.Type);
            Assert.NotNull(n.ReleasedAtUtc);
            Assert.NotNull(n.EventId);
        });

        // 自白帖：1 篇，completed 后翻 published，作者为 agent 本人。
        var posts = await db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
            .Find(Builders<ForumPostRecord>.Filter.Eq(p => p.AuthorSubId, target)).ToListAsync();
        Assert.Single(posts);
        Assert.Equal("published", posts[0].State);

        // AI 标记：agent_personas.state=eliminated。
        var persona = await db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .Find(Builders<AgentPersonaRecord>.Filter.Eq(p => p.AgentUserId, target)).FirstAsync();
        Assert.Equal(AgentPersonaState.Eliminated, persona.State);

        // tally → pending_reveal；accusations → settled。
        var tally = await db.GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName)
            .Find(Builders<ArenaTargetTallyRecord>.Filter.Eq(t => t.TargetUserId, target)).FirstAsync();
        Assert.Equal("pending_reveal", tally.Status);
        var accCount = await db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName)
            .CountDocumentsAsync(Builders<ArenaAccusationRecord>.Filter
                .Eq(a => a.TargetUserId, target) & Builders<ArenaAccusationRecord>.Filter.Eq(a => a.Status, "settled"));
        Assert.Equal(3, accCount);
    }

    [Fact]
    public async Task RevealWeek_human目标_零通知_void_human_且不计成功()
    {
        await _fx.ClearAsync();
        const string week = "2026-07-08";
        const string target = "human-pipeline";
        SeedTally(week, target, 3);
        SeedAccusations(week, target, "b1", "b2");

        var svc = CreateService();
        var outcome = await svc.RevealWeekAsync(week);
        Assert.Equal(RevealOutcome.Processed, outcome);

        var db = Db();
        // 命中 miss 事件：-5 / notifyEligible=false。
        var events = await db.GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .Find(Builders<ArenaPointsEventRecord>.Filter.Eq(e => e.WeekId, week)).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal("accusation_miss", e.Type);
            Assert.Equal(-5, e.Delta);
            Assert.False(e.NotifyEligible);
        });

        // 零通知（human 不揭露）。
        var job = await db.GetCollection<ArenaRevealJobRecord>(ArenaMongoSetup.RevealJobsCollectionName)
            .Find(Builders<ArenaRevealJobRecord>.Filter.Eq(j => j.WeekKey, week)).FirstAsync();
        var notifs = await db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName)
            .CountDocumentsAsync(Builders<InAppNotificationRecord>.Filter.Eq(n => n.BatchId, job.Id));
        Assert.Equal(0, notifs);

        // tally → void_human。
        var tally = await db.GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName)
            .Find(Builders<ArenaTargetTallyRecord>.Filter.Eq(t => t.TargetUserId, target)).FirstAsync();
        Assert.Equal("void_human", tally.Status);

        // 无人格 persona（human 不在 agent_personas），不应被标记。
        var personaCount = await db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName)
            .CountDocumentsAsync(Builders<AgentPersonaRecord>.Filter.Eq(p => p.AgentUserId, target));
        Assert.Equal(0, personaCount);
    }

    [Fact]
    public async Task RevealWeek_未达阈值目标不产生结算计划()
    {
        await _fx.ClearAsync();
        const string week = "2026-07-15";
        const string target = "agent-low";
        SeedPersona(target);
        // 未达阈值（状态仍为 active，而非 threshold_reached）→ prepare 不应处理。
        SeedTally(week, target, 1, "active");
        SeedAccusations(week, target, "c1");

        var svc = CreateService();
        var outcome = await svc.RevealWeekAsync(week);
        Assert.Equal(RevealOutcome.Processed, outcome);

        var db = Db();
        var events = await db.GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .CountDocumentsAsync(Builders<ArenaPointsEventRecord>.Filter.Eq(e => e.WeekId, week));
        Assert.Equal(0, events); // 未达阈值，零事件、零通知。
        var notifs = await db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName)
            .CountDocumentsAsync(FilterDefinition<InAppNotificationRecord>.Empty);
        Assert.Equal(0, notifs);
    }

    [Fact]
    public async Task RevealWeek_agent目标_LLM正文命中屏蔽词_降级静态安全模板后发布且门禁生效()
    {
        await _fx.ClearAsync();
        const string week = "2026-07-22";
        const string target = "agent-blocked";
        const string blockedWord = "违禁词XYZ";
        SeedPersona(target);
        SeedTally(week, target, 3);
        SeedAccusations(week, target, "d1", "d2", "d3");

        // 真实内容安全过滤器：配置违禁词（reject 处理）→ 命中非 Pass。
        IForumBlockedWordFilter filter = new ForumBlockedWordFilter(new OptionsStub<ForumBlockedWordsOptions>(
            new ForumBlockedWordsOptions
            {
                Enabled = true,
                Words = new List<string> { blockedWord },
                Handling = ForumBlockedWordsOptions.HandlingReject,
            }));
        // 生成器返回含违禁词的 LLM 正文（source=llm）。
        var svc = CreateService(
            new StubConfessionGenerator($"这是一段包含{blockedWord}的违规自白正文。", ArenaConfessionGeneration.Llm),
            filter);

        var outcome = await svc.RevealWeekAsync(week);
        Assert.Equal(RevealOutcome.Processed, outcome);

        var db = Db();
        var job = await db.GetCollection<ArenaRevealJobRecord>(ArenaMongoSetup.RevealJobsCollectionName)
            .Find(Builders<ArenaRevealJobRecord>.Filter.Eq(j => j.WeekKey, week)).FirstAsync();
        Assert.Equal("completed", job.State);

        // D8 P0 闸门：被屏蔽的 LLM 正文不得发布；应降级为静态安全模板并发布。
        var posts = await db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
            .Find(Builders<ForumPostRecord>.Filter.Eq(p => p.AuthorSubId, target)).ToListAsync();
        Assert.Single(posts);
        Assert.Equal("published", posts[0].State);
        Assert.Equal(ArenaConfessionTemplate.StaticBody(target), posts[0].Body);
        Assert.DoesNotContain(blockedWord, posts[0].Body);

        // 结算仍正常：3 条 hit 事件 + 3 条 +50 通知（已释放）。
        var events = await db.GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .CountDocumentsAsync(Builders<ArenaPointsEventRecord>.Filter.Eq(e => e.WeekId, week));
        Assert.Equal(3, events);
        var notifCount = await db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName)
            .CountDocumentsAsync(Builders<InAppNotificationRecord>.Filter.Eq(n => n.BatchId, job.Id));
        Assert.Equal(3, notifCount);
    }
}
