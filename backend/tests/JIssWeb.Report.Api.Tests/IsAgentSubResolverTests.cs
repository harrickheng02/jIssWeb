using System.Threading.Tasks;
using JIssWeb.Report.Api.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// IsAgentSubResolver 测试：AI vs 人类判定走 agent_personas.AgentUserId join（绝不依赖 accountType claim）。
/// </summary>
[Collection("ReportDashboard")]
public class IsAgentSubResolverTests
{
    private readonly ReportDashboardFixture _fx;

    public IsAgentSubResolverTests(ReportDashboardFixture fx) => _fx = fx;

    [Fact]
    public async Task IsAgentSubAsync_已注册AgentSub_返回true()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        var personas = db.GetCollection<BsonDocument>("agent_personas");
        await personas.InsertOneAsync(new BsonDocument
        {
            { "_id", "p1" }, { "PersonaId", "per1" }, { "AgentUserId", "agent-xyz" }, { "State", "active" },
        });

        var resolver = new IsAgentSubResolver(db);
        Assert.True(await resolver.IsAgentSubAsync("agent-xyz"));
    }

    [Fact]
    public async Task IsAgentSubAsync_非AgentSub_返回false()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        var personas = db.GetCollection<BsonDocument>("agent_personas");
        await personas.InsertOneAsync(new BsonDocument
        {
            { "_id", "p1" }, { "PersonaId", "per1" }, { "AgentUserId", "agent-xyz" }, { "State", "active" },
        });

        var resolver = new IsAgentSubResolver(db);
        Assert.False(await resolver.IsAgentSubAsync("human-abc"));
    }

    [Fact]
    public async Task IsAgentSubAsync_空sub_返回false()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        var resolver = new IsAgentSubResolver(db);
        Assert.False(await resolver.IsAgentSubAsync(""));
        Assert.False(await resolver.IsAgentSubAsync(null!));
    }

    [Fact]
    public async Task IsAgentSubAsync_重复调用_结果一致且命中缓存()
    {
        var db = _fx.Database();
        await _fx.ClearAsync();
        var personas = db.GetCollection<BsonDocument>("agent_personas");
        await personas.InsertOneAsync(new BsonDocument
        {
            { "_id", "p1" }, { "PersonaId", "per1" }, { "AgentUserId", "agent-xyz" }, { "State", "active" },
        });

        var resolver = new IsAgentSubResolver(db);
        var first = await resolver.IsAgentSubAsync("agent-xyz");
        var second = await resolver.IsAgentSubAsync("agent-xyz");
        Assert.True(first);
        Assert.True(second);
    }
}
