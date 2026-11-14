using JIssWeb.Report.Api.Models;
using JIssWeb.Report.Api.Services;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

[Collection("HallOfTuring")]
public sealed class HallOfTuringServiceTests
{
    private readonly HallOfTuringFixture _fx;

    public HallOfTuringServiceTests(HallOfTuringFixture fx) => _fx = fx;

    private ArenaHallOfTuringService CreateService() =>
        new(_fx.Database(), new MaskedHunterNameResolver());

    [Fact]
    public async Task HallAsync_仅含已揭露奖章账号_且倒序_且匿名幸存者计数()
    {
        var vm = await CreateService().HallAsync();

        // 仅 per1、per2（eliminated + 奖章）；per3（幸存者）不在列表；per4（eliminated 无奖章）不在列表。
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("per2", vm.Items[0].PersonaId); // MedalAwardedAtUtc 更近（now-3d）
        Assert.Equal("per1", vm.Items[1].PersonaId); // now-5d
        Assert.Equal(1, vm.ActiveSurvivorCount);       // per3 幸存者
    }

    [Fact]
    public async Task HallAsync_卡片派生_猎手脱敏_驱动模型_世代_话题_互动()
    {
        var vm = await CreateService().HallAsync();
        var per1 = vm.Items.Single(x => x.PersonaId == "per1");
        var per2 = vm.Items.Single(x => x.PersonaId == "per2");

        // per1
        Assert.Equal(400, per1.FinalBounty);
        Assert.Equal("Gen-1", per1.Generation);
        Assert.Equal(30, per1.SurvivalDays);
        Assert.True(per1.Discovered);
        Assert.Equal(2, per1.DiscoveredBy.Count);     // 两位猎手 → 两个脱敏代号
        Assert.All(per1.DiscoveredBy, d => Assert.StartsWith("侦探#", d));
        Assert.Equal("DeepSeek-V3", per1.DrivingModel); // LlmProfileId 命中
        Assert.Equal(3, per1.TopTopics.Count);
        Assert.Equal("AI伦理", per1.TopTopics[0].Tag);
        Assert.Equal(3, per1.TopTopics[0].Count);
        Assert.Equal(21, per1.InteractionSummary.Likes);
        Assert.Equal(380, per1.InteractionSummary.Views);

        // per2：LlmProfileId 为空 → 降级 Model；SurvivalDays=0 → 由创建时间推算（约 17 天）
        Assert.Equal("doubao", per2.DrivingModel);
        Assert.Equal(200, per2.FinalBounty);
        var per2Hunter = Assert.Single(per2.DiscoveredBy);
        Assert.StartsWith("侦探#", per2Hunter);
        Assert.True(per2.SurvivalDays is >= 16 and <= 18);
    }

    [Fact]
    public async Task ProfileAsync_已揭露奖章账号_返回全量_含自白帖与近危险()
    {
        var vm = await CreateService().ProfileAsync("per1");
        Assert.NotNull(vm);
        Assert.Equal("per1", vm!.PersonaId);
        Assert.Equal(30, vm.SurvivalDays);

        // 时间线：5 帖 + 2 回复 = 7 项，按时间升序
        Assert.Equal(7, vm.Timeline.Count);

        // 自白帖（=触发帖）
        Assert.NotNull(vm.Confession);
        Assert.True(vm.Confession!.IsConfession);
        Assert.Equal("我是一个 AI", vm.Confession.Body);
        Assert.NotNull(vm.TriggerPost);
        Assert.Equal(vm.Confession.Id, vm.TriggerPost!.Id);

        // 近危险：仅近 3 周且 Status==active（tt2、tt1；tt3 过期、tt4 非 active 排除）
        Assert.Equal(2, vm.NearDanger.Count);
        Assert.Equal("2026-11-09", vm.NearDanger[0].WeekKey);
        Assert.Equal(3, vm.NearDanger[0].Count);
        Assert.Equal("2026-11-02", vm.NearDanger[1].WeekKey);
        Assert.Equal(2, vm.NearDanger[1].Count);

        Assert.Equal(380, vm.InteractionSummary.Views);
    }

    [Fact]
    public async Task ProfileAsync_幸存者或非奖章或不存在_返回null()
    {
        Assert.Null(await CreateService().ProfileAsync("per3")); // 幸存者（active）
        Assert.Null(await CreateService().ProfileAsync("per4")); // eliminated 但无奖章
        Assert.Null(await CreateService().ProfileAsync("ghost"));
    }

    [Fact]
    public async Task AiqiTrendAsync_全局周序列_升序_无LLM()
    {
        var trend = await CreateService().AiqiTrendAsync(12);
        Assert.Equal(4, trend.Count);
        Assert.Equal("2026-10-20", trend[0].WeekKey);
        Assert.Equal(0.75, trend[0].Aiqi);
        Assert.Equal("2026-11-10", trend[3].WeekKey);
        Assert.Equal(150, trend[3].ActiveUsers);
    }

    [Fact]
    public async Task 只读纪律_聚合前后集合文档数不变()
    {
        var db = _fx.Database();
        var names = new[] { "agent_personas", "agent_llm_profiles", "arena_bounty_states", "arena_points_events", "forum_posts", "forum_replies", "arena_reveal_jobs", "arena_target_tallies", "arena_aiqi_results" };
        var before = new Dictionary<string, long>();
        foreach (var n in names)
            before[n] = await db.GetCollection<MongoDB.Bson.BsonDocument>(n).CountDocumentsAsync(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Empty);

        await CreateService().HallAsync();
        await CreateService().ProfileAsync("per1");
        await CreateService().AiqiTrendAsync(12);

        foreach (var n in names)
        {
            var after = await db.GetCollection<MongoDB.Bson.BsonDocument>(n).CountDocumentsAsync(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Empty);
            Assert.Equal(before[n], after);
        }
    }
}
