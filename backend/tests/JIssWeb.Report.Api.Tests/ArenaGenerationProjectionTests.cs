using System;
using System.Linq;
using System.Threading.Tasks;
using JIssWeb.Report.Api.Models;
using JIssWeb.Report.Api.Services;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>#38 只读投影：世代存活曲线面板 + 名人堂血统。验证聚合正确 + 只读纪律。</summary>
[Collection("ReportDashboard")]
public class ArenaGenerationProjectionTests
{
    private readonly ReportDashboardFixture _fx;
    public ArenaGenerationProjectionTests(ReportDashboardFixture fx) => _fx = fx;

    private static readonly string[] Collections =
    {
        "agent_personas", "agent_scheduler_state", "agent_llm_profiles", "arena_reveal_jobs",
        "arena_netflow_results", "arena_aiqi_results", "user_points_ledger", "user_points_transactions",
        "arena_accusations", "arena_bounty_states", "forum_posts", "forum_replies", "forum_reports",
    };

    private async Task<long> CountAllAsync(IMongoDatabase db)
    {
        long total = 0;
        foreach (var name in Collections)
            total += await db.GetCollection<BsonDocument>(name).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        return total;
    }

    [Fact]
    public async Task BuildAsync_GenerationSurvivalPanel_GroupsByGeneration_AndReadOnly()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        var now = DateTime.UtcNow;
        await db.GetCollection<BsonDocument>("agent_personas").InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "g1a" }, { "PersonaId", "g1a" }, { "AgentUserId", "g1a" }, { "State", "active" }, { "Generation", 1 }, { "SurvivalDays", 10 }, { "CreatedAtUtc", now.AddDays(-10) } },
            new BsonDocument { { "_id", "g1b" }, { "PersonaId", "g1b" }, { "AgentUserId", "g1b" }, { "State", "active" }, { "Generation", 1 }, { "SurvivalDays", 20 }, { "CreatedAtUtc", now.AddDays(-20) } },
            new BsonDocument { { "_id", "g2a" }, { "PersonaId", "g2a" }, { "AgentUserId", "g2a" }, { "State", "active" }, { "Generation", 2 }, { "SurvivalDays", 0 }, { "CreatedAtUtc", now.AddDays(-14) } },
        });
        var before = await CountAllAsync(db);

        var options = Options.Create(new ArenaDashboardRevealOptions { RevealHourLocal = 22, TimeZoneId = "Asia/Shanghai" });
        var service = new ArenaDashboardService(db, options, new IsAgentSubResolver(db));
        var vm = await service.BuildAsync();

        var panel = vm.GenerationSurvival;
        Assert.Equal(2, panel.Points.Count);
        var gen1 = panel.Points.Single(x => x.Generation == 1);
        Assert.Equal(2, gen1.Count);
        Assert.Equal(15.0, gen1.AvgSurvivalDays, 1);
        var gen2 = panel.Points.Single(x => x.Generation == 2);
        Assert.Equal(1, gen2.Count);
        Assert.InRange(gen2.AvgSurvivalDays, 13.9, 14.1); // SurvivalDays=0 → 由 CreatedAtUtc 推导

        Assert.Equal(before, await CountAllAsync(db)); // 只读
    }

    [Fact]
    public async Task BuildAsync_EmptyPersonas_GenerationPanelEmpty_NotThrow()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        var options = Options.Create(new ArenaDashboardRevealOptions { RevealHourLocal = 22, TimeZoneId = "Asia/Shanghai" });
        var service = new ArenaDashboardService(db, options, new IsAgentSubResolver(db));
        var vm = await service.BuildAsync();
        Assert.Empty(vm.GenerationSurvival.Points);
    }

    [Fact]
    public async Task HallAsync_GenerationAndLineage_ReadOnly()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        var now = DateTime.UtcNow;
        await db.GetCollection<BsonDocument>("agent_personas").InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "root" }, { "PersonaId", "root" }, { "Nickname", "始祖" }, { "AgentUserId", "root-u" }, { "Model", "deepseek" }, { "Generation", 1 }, { "State", "eliminated" }, { "CreatedAtUtc", now.AddDays(-60) }, { "SurvivalDays", 30 } },
            new BsonDocument { { "_id", "child" }, { "PersonaId", "child" }, { "Nickname", "后代" }, { "AgentUserId", "child-u" }, { "Model", "deepseek" }, { "Generation", 2 }, { "InheritedFrom", new BsonArray { "root" } }, { "State", "eliminated" }, { "CreatedAtUtc", now.AddDays(-30) }, { "SurvivalDays", 20 } },
        });
        await db.GetCollection<BsonDocument>("arena_bounty_states").InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "root-u" }, { "AgentUserId", "root-u" }, { "MedalAwardedAtUtc", now.AddDays(-30) }, { "CurrentBounty", 400 }, { "BountyCap", 400 } },
            new BsonDocument { { "_id", "child-u" }, { "AgentUserId", "child-u" }, { "MedalAwardedAtUtc", now.AddDays(-5) }, { "CurrentBounty", 400 }, { "BountyCap", 400 } },
        });
        var before = await CountAllAsync(db);

        var service = new ArenaHallOfTuringService(db, new MaskedHunterNameResolver());
        var vm = await service.HallAsync();
        var child = vm.Items.Single(x => x.PersonaId == "child");
        Assert.Equal("Gen-2", child.Generation);
        Assert.Contains("始祖", child.Lineage);
        var root = vm.Items.Single(x => x.PersonaId == "root");
        Assert.Equal("Gen-1", root.Generation);
        Assert.Empty(root.Lineage);

        Assert.Equal(before, await CountAllAsync(db)); // 只读
    }
}
