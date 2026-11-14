using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JIssWeb.Report.Api.Models;
using JIssWeb.Report.Api.Services;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// ArenaDashboardService 只读聚合集成测试（Mongo2Go 内存实例）。
/// 种子 13 个集合，断言 5 个面板聚合正确，并验证「只读纪律」：BuildAsync 前后集合文档数不变。
/// </summary>
[Collection("ReportDashboard")]
public class ArenaDashboardServiceIntegrationTests
{
    private readonly ReportDashboardFixture _fx;

    // 13 个只读集合名（与 DashboardCollectionNames 一致；internal 不可跨程序集访问，此处稳定镜像）。
    private static readonly string[] Collections =
    {
        "agent_personas", "agent_scheduler_state", "agent_llm_profiles", "arena_reveal_jobs",
        "arena_netflow_results", "arena_aiqi_results", "user_points_ledger", "user_points_transactions",
        "arena_accusations", "arena_bounty_states", "forum_posts", "forum_replies", "forum_reports",
    };

    public ArenaDashboardServiceIntegrationTests(ReportDashboardFixture fx) => _fx = fx;

    private static DateTime StartOfWeekUtc(DateTimeOffset now)
    {
        var tz = ArenaDashboardSchedule.Resolve("Asia/Shanghai");
        var local = TimeZoneInfo.ConvertTime(now, tz);
        var diff = (7 + (int)local.DayOfWeek - 1) % 7;
        var mondayLocal = local.Date.AddDays(-diff);
        return new DateTimeOffset(mondayLocal, tz.GetUtcOffset(mondayLocal)).UtcDateTime;
    }

    private static string AddDays(string yyyymmdd, int days)
    {
        var d = DateTime.ParseExact(yyyymmdd, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return d.AddDays(days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task SeedAsync(IMongoDatabase db)
    {
        var now = DateTimeOffset.UtcNow;
        var weekKey = ArenaDashboardSchedule.For(now);
        var sow = StartOfWeekUtc(now);
        var oldWeek = AddDays(weekKey, -7);
        var midLastWeek = sow.AddDays(-3);
        var midThisWeek = sow.AddDays(1);

        var agentPersonas = db.GetCollection<BsonDocument>("agent_personas");
        await agentPersonas.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "p1" }, { "PersonaId", "per1" }, { "AgentUserId", "agent-sub-1" }, { "State", "active" } },
            new BsonDocument { { "_id", "p2" }, { "PersonaId", "per2" }, { "AgentUserId", "agent-sub-2" }, { "State", "inactive" } },
            new BsonDocument { { "_id", "p3" }, { "PersonaId", "per3" }, { "AgentUserId", "agent-sub-3" }, { "State", "active" } },
        });

        var sched = db.GetCollection<BsonDocument>("agent_scheduler_state");
        await sched.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "s1" }, { "PersonaId", "per1" }, { "AgentUserId", "agent-sub-1" }, { "ConsecutiveFailures", 0 }, { "LastError", BsonNull.Value }, { "UpdatedAtUtc", now.AddHours(-1).UtcDateTime } },
            new BsonDocument { { "_id", "s2" }, { "PersonaId", "per2" }, { "AgentUserId", "agent-sub-2" }, { "ConsecutiveFailures", 3 }, { "LastError", "boom" }, { "UpdatedAtUtc", now.AddHours(-2).UtcDateTime } },
        });

        var llm = db.GetCollection<BsonDocument>("agent_llm_profiles");
        await llm.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "l1" }, { "Name", "DeepSeek" }, { "Enabled", true } },
        });

        var jobs = db.GetCollection<BsonDocument>("arena_reveal_jobs");
        await jobs.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "j-done" }, { "State", "completed" } },
            new BsonDocument { { "_id", "j-pending" }, { "State", "pending" } },
        });

        // 4 个 Accounting 集合：_id 为原生 ObjectId（与生产一致，省略 _id 让 Mongo 自动生成）。
        var netflow = db.GetCollection<BsonDocument>("arena_netflow_results");
        await netflow.InsertManyAsync(new[]
        {
            new BsonDocument { { "WeekKey", weekKey }, { "NetFlow", 100 }, { "InflationAlert", false } },
            new BsonDocument { { "WeekKey", oldWeek }, { "NetFlow", 999 }, { "InflationAlert", false } },
        });

        var aiqi = db.GetCollection<BsonDocument>("arena_aiqi_results");
        await aiqi.InsertManyAsync(new[]
        {
            new BsonDocument { { "WeekKey", weekKey }, { "Aiqi", 0.85 }, { "SampleSufficient", true } },
            new BsonDocument { { "WeekKey", oldWeek }, { "Aiqi", 0.1 }, { "SampleSufficient", false } },
        });

        var ledger = db.GetCollection<BsonDocument>("user_points_ledger");
        await ledger.InsertManyAsync(new[]
        {
            new BsonDocument { { "Balance", 500 } },
            new BsonDocument { { "Balance", 300 } },
        });

        var tx = db.GetCollection<BsonDocument>("user_points_transactions");
        await tx.InsertManyAsync(new[]
        {
            new BsonDocument { { "Delta", 50 }, { "OccurredAtUtc", now.AddHours(-3).UtcDateTime } },
            new BsonDocument { { "Delta", 30 }, { "OccurredAtUtc", now.AddHours(-3).UtcDateTime } },
            new BsonDocument { { "Delta", 20 }, { "OccurredAtUtc", midLastWeek } },
        });

        var acc = db.GetCollection<BsonDocument>("arena_accusations");
        await acc.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "a1" }, { "WeekKey", weekKey }, { "AccuserId", "det1" } },
            new BsonDocument { { "_id", "a2" }, { "WeekKey", weekKey }, { "AccuserId", "det1" } },
            new BsonDocument { { "_id", "a3" }, { "WeekKey", weekKey }, { "AccuserId", "det2" } },
            new BsonDocument { { "_id", "a4" }, { "WeekKey", oldWeek }, { "AccuserId", "det_old" } },
        });

        var bounty = db.GetCollection<BsonDocument>("arena_bounty_states");
        await bounty.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "b1" }, { "AgentUserId", "agent-sub-1" }, { "SurvivedWeeks", 5 }, { "CurrentBounty", 1000 }, { "IsLegendaryPrey", true } },
            new BsonDocument { { "_id", "b2" }, { "AgentUserId", "agent-sub-2" }, { "SurvivedWeeks", 2 }, { "CurrentBounty", 200 }, { "IsLegendaryPrey", false } },
        });

        var posts = db.GetCollection<BsonDocument>("forum_posts");
        await posts.InsertManyAsync(new[]
        {
            // agent-sub-1 的帖必须足够「新」（近 2 天内），否则会被 timeout 派生判为沉默 agent。
            new BsonDocument { { "_id", "fp1" }, { "AuthorSubId", "agent-sub-1" }, { "CreatedAtUtc", now.AddHours(-3).UtcDateTime } },
            new BsonDocument { { "_id", "fp2" }, { "AuthorSubId", "human-1" }, { "CreatedAtUtc", midThisWeek } },
            new BsonDocument { { "_id", "fp3" }, { "AuthorSubId", "human-2" }, { "CreatedAtUtc", midThisWeek } },
            new BsonDocument { { "_id", "fp4" }, { "AuthorSubId", "human-old" }, { "CreatedAtUtc", sow.AddDays(-10) } },
        });

        var replies = db.GetCollection<BsonDocument>("forum_replies");
        await replies.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "fr1" }, { "AuthorSubId", "human-1" }, { "CreatedAtUtc", midThisWeek } },
        });

        var reports = db.GetCollection<BsonDocument>("forum_reports");
        await reports.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "r1" }, { "Reason", "spam" }, { "Status", "pending" } },
            new BsonDocument { { "_id", "r2" }, { "Reason", "ok" }, { "Status", "resolved" } },
            new BsonDocument { { "_id", "r3" }, { "Reason", BsonNull.Value }, { "Status", "pending" } },
        });
    }

    private static async Task<long> CountAllAsync(IMongoDatabase db)
    {
        long total = 0;
        foreach (var name in Collections)
            total += await db.GetCollection<BsonDocument>(name).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        return total;
    }

    [Fact]
    public async Task BuildAsync_聚合五面板_与种子一致且只读()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        await SeedAsync(db);
        var before = await CountAllAsync(db);

        var options = Options.Create(new ArenaDashboardRevealOptions { RevealHourLocal = 22, TimeZoneId = "Asia/Shanghai" });
        var resolver = new IsAgentSubResolver(db);
        var service = new ArenaDashboardService(db, options, resolver);

        var vm = await service.BuildAsync();

        // ── 只读纪律 ──
        var after = await CountAllAsync(db);
        Assert.Equal(before, after);

        // ── 系统健康 ──
        Assert.Equal(2, vm.SystemHealth.AgentAliveCount); // active: p1, p3
        Assert.Equal(3, vm.SystemHealth.AgentTotalCount);
        Assert.Equal("degraded", vm.SystemHealth.SchedulerStatus); // s2 有错
        Assert.NotNull(vm.SystemHealth.SchedulerLastTickAtUtc);
        Assert.Single(vm.SystemHealth.LlmProfiles);
        Assert.Equal("DeepSeek", vm.SystemHealth.LlmProfiles[0].Name);
        Assert.True(vm.SystemHealth.LlmProfiles[0].Enabled);
        Assert.Single(vm.SystemHealth.PendingReveal);
        Assert.Equal("pending", vm.SystemHealth.PendingReveal[0].State);

        // ── 经济 ──
        Assert.Equal(100, vm.Economy.WeeklyNetFlow); // 取最新周（oldWeek 的 999 被忽略）
        Assert.Equal(0.85, vm.Economy.Aiqi);
        Assert.True(vm.Economy.AiqiConfident);
        Assert.Equal(80, vm.Economy.PointsInjectionDensity); // 50+30 本周
        Assert.Equal(20, vm.Economy.LastWeekDensity); // 上周 20
        Assert.Equal("warning", vm.Economy.NetFlowLevel); // netflow>0

        // ── 游戏 ──
        Assert.Equal(3, vm.Gameplay.WeeklyAccusations); // 仅本周 3 条
        Assert.Equal(2, vm.Gameplay.TopDetectives.Count);
        Assert.Equal("det1", vm.Gameplay.TopDetectives[0].UserId);
        Assert.Equal(2, vm.Gameplay.TopDetectives[0].Count);
        Assert.Single(vm.Gameplay.LegendaryPrey);
        Assert.Equal("agent-sub-1", vm.Gameplay.LegendaryPrey[0].PersonaId);
        Assert.Equal(1000, vm.Gameplay.LegendaryPrey[0].CurrentBounty);
        Assert.True(vm.Gameplay.NextRevealAtUtc > DateTime.UtcNow);
        Assert.Equal(DayOfWeek.Friday,
            TimeZoneInfo.ConvertTime(vm.Gameplay.NextRevealAtUtc, ArenaDashboardSchedule.Resolve("Asia/Shanghai")).DayOfWeek);

        // ── 待处理队列 ──
        Assert.Equal(2, vm.Queue.PendingReports.Count); // 两条 pending（r2 resolved 不计）
        Assert.Contains(vm.Queue.PendingReports, r => r.ReportId == "r1" && r.Reason == "spam");
        Assert.Contains(vm.Queue.PendingReports, r => r.ReportId == "r3" && r.Reason == null);
        // 1 条 error（s2）+ 1 条 timeout（p3 无发帖）
        Assert.Equal(2, vm.Queue.AgentAlerts.Count);
        Assert.Contains(vm.Queue.AgentAlerts, a => a.Type == "error" && a.PersonaId == "per2" && a.Detail == "boom");
        Assert.Contains(vm.Queue.AgentAlerts, a => a.Type == "timeout" && a.PersonaId == "agent-sub-3");

        // ── 社区 ──
        Assert.Equal(3, vm.Community.WeeklyPostsTotal); // 本周 3 帖（human-old 不计）
        Assert.Equal(1, vm.Community.WeeklyPostsAi); // agent-sub-1
        Assert.Equal(2, vm.Community.WeeklyPostsHuman); // human-1, human-2
        Assert.Equal(2, vm.Community.WeeklyActiveHumans); // human-1, human-2（去重；agent 不计）
    }

    [Fact]
    public async Task BuildAsync_空库_返回零化面板不抛错()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        var options = Options.Create(new ArenaDashboardRevealOptions { RevealHourLocal = 22, TimeZoneId = "Asia/Shanghai" });
        var resolver = new IsAgentSubResolver(db);
        var service = new ArenaDashboardService(db, options, resolver);

        var vm = await service.BuildAsync();

        Assert.Equal(0, vm.SystemHealth.AgentTotalCount);
        Assert.Equal(0, vm.Economy.WeeklyNetFlow);
        Assert.Equal(0, vm.Gameplay.WeeklyAccusations);
        Assert.Empty(vm.Queue.PendingReports);
        Assert.Empty(vm.Queue.AgentAlerts);
        Assert.Equal(0, vm.Community.WeeklyPostsTotal);
        Assert.True(vm.Gameplay.NextRevealAtUtc > DateTime.UtcNow);
    }
}
