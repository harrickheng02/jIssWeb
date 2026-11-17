using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>固定结论的门禁替身。</summary>
internal sealed class FixedModerationGate : IContentModerationGate
{
    public ModerationDecision Decision { get; set; } = ModerationDecision.Pass;
    public int Calls { get; private set; }

    public Task<ModerationDecision> EvaluateAsync(ModerationRequest request, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Decision);
    }
}

/// <summary>始终抛异常的门禁替身，用于验证直写路径的异常隔离。</summary>
internal sealed class ThrowingModerationGate : IContentModerationGate
{
    public Task<ModerationDecision> EvaluateAsync(ModerationRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException("gate boom");
}

/// <summary>
/// AI 版主阶段一：两个绕过 Controller 的直写路径（公开质询、自白帖）。
/// 门禁异常 SHALL NOT 阻断业务流程。
/// </summary>
[Collection("ArenaReveal")]
public sealed class ArenaContentModerationTests
{
    private readonly ArenaRevealIntegrationFixture _fx;

    public ArenaContentModerationTests(ArenaRevealIntegrationFixture fx) => _fx = fx;

    private IMongoDatabase Db() => _fx.GetClient().GetDatabase(_fx.DatabaseName);

    private ArenaSkillEffectsService CreateSkillEffects(IContentModerationGate gate)
        => new(Db(), gate);

    private ArenaRevealService CreateReveal(IContentModerationGate gate, IArenaConfessionGenerator? confession = null)
    {
        var client = _fx.GetClient();
        IOptions<MongoSettings> opts = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });
        var bounty = new ArenaBountyService(client, opts,
            new OptionsStub<ArenaBountyOptions>(new ArenaBountyOptions()), NullLogger<ArenaBountyService>.Instance);
        return new ArenaRevealService(
            client, opts,
            new OptionsStub<ArenaRevealOptions>(new ArenaRevealOptions()),
            confession ?? new StubConfessionGenerator("我是自白正文。"),
            new PassBlockedWordFilter(),
            NullLogger<ArenaRevealService>.Instance,
            bounty,
            gate);
    }

    private void SeedPersona(string agentUserId)
        => Db().GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName).InsertOne(new AgentPersonaRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            PersonaId = "p-" + agentUserId,
            AgentUserId = agentUserId,
            Nickname = agentUserId,
            State = AgentPersonaState.Active,
            CreatedAtUtc = DateTime.UtcNow,
        });

    [Fact]
    public async Task Public_inquiry_flagged_by_moderation_is_pending_review()
    {
        await _fx.ClearAsync();
        var gate = new FixedModerationGate
        {
            Decision = new ModerationDecision("pending_review", true, new ModerationVerdict(ForumModerationVerdicts.Spam, 0.95, "广告")),
        };
        var svc = CreateSkillEffects(gate);

        var postId = await svc.PublicInquiryAsync("caller-1", "target-1", "你是不是 AI？加我微信");

        Assert.Equal(1, gate.Calls);
        var post = await Db().GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
            .Find(x => x.Id == postId).FirstOrDefaultAsync();
        Assert.Equal("pending_review", post!.State);

        // 业务流程不被阻断：回应任务照常创建。
        var task = await Db().GetCollection<ArenaInquiryTaskRecord>("arena_inquiry_tasks")
            .Find(x => x.InquiryPostId == postId).FirstOrDefaultAsync();
        Assert.NotNull(task);
    }

    [Fact]
    public async Task Public_inquiry_survives_gate_exception()
    {
        await _fx.ClearAsync();
        var svc = CreateSkillEffects(new ThrowingModerationGate());

        var postId = await svc.PublicInquiryAsync("caller-2", "target-2", "正常质询内容");

        var post = await Db().GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
            .Find(x => x.Id == postId).FirstOrDefaultAsync();
        Assert.Equal("published", post!.State);
    }

    [Fact]
    public async Task Confession_falls_back_to_static_template_when_flagged()
    {
        await _fx.ClearAsync();
        const string week = "2026-08-05";
        const string target = "agent-confession-mod";
        SeedPersona(target);
        SeedTally(week, target, 3);
        SeedAccusations(week, target, "a1", "a2", "a3");

        var gate = new FixedModerationGate
        {
            Decision = new ModerationDecision("pending_review", true, new ModerationVerdict(ForumModerationVerdicts.Spam, 0.95, "广告")),
        };
        var svc = CreateReveal(gate, new StubConfessionGenerator("点我主页领取福利"));

        var outcome = await svc.RevealWeekAsync(week);

        Assert.Equal(RevealOutcome.Processed, outcome);
        var post = await Db().GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
            .Find(x => x.AuthorSubId == target).FirstOrDefaultAsync();
        Assert.NotNull(post);
        Assert.Equal(ArenaConfessionTemplate.StaticBody(target), post!.Body);
    }

    [Fact]
    public async Task Confession_survives_gate_exception()
    {
        await _fx.ClearAsync();
        const string week = "2026-08-06";
        const string target = "agent-confession-boom";
        SeedPersona(target);
        SeedTally(week, target, 3);
        SeedAccusations(week, target, "b1", "b2", "b3");

        var svc = CreateReveal(new ThrowingModerationGate(), new StubConfessionGenerator("生成的正常自白"));

        var outcome = await svc.RevealWeekAsync(week);

        Assert.Equal(RevealOutcome.Processed, outcome);
        var post = await Db().GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName)
            .Find(x => x.AuthorSubId == target).FirstOrDefaultAsync();
        Assert.Equal("生成的正常自白", post!.Body);
    }

    private void SeedTally(string week, string target, int count)
        => Db().GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName).InsertOne(new ArenaTargetTallyRecord
        {
            WeekKey = week,
            TargetUserId = target,
            Count = count,
            Status = "threshold_reached",
            UpdatedAtUtc = DateTime.UtcNow,
        });

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
}
