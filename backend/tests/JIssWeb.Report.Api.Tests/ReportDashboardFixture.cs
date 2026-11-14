using MongoDB.Bson;
using MongoDB.Driver;
using Mongo2Go;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// 运营仪表盘集成测试夹具：Mongo2Go 内存实例（零外部依赖，CI 自包含）。
/// 仅读 jissweb_model 等效库，测试前后集合文档数不变 —— 验证只读纪律。
/// </summary>
public sealed class ReportDashboardFixture : IAsyncLifetime
{
    private MongoDbRunner? _runner;

    public string ConnectionString { get; private set; } = "";
    public string DatabaseName { get; private set; } = "";

    // 13 个只读集合名（与 DashboardCollectionNames 一致；internal 不可跨程序集访问，此处稳定镜像）。
    private static readonly string[] Collections =
    {
        "agent_personas", "agent_scheduler_state", "agent_llm_profiles", "arena_reveal_jobs",
        "arena_netflow_results", "arena_aiqi_results", "user_points_ledger", "user_points_transactions",
        "arena_accusations", "arena_bounty_states", "forum_posts", "forum_replies", "forum_reports",
    };

    public IMongoDatabase Database() => new MongoClient(ConnectionString).GetDatabase(DatabaseName);

    /// <summary>
    /// 清空 13 个集合，使每个测试自包含、幂等于任意运行顺序与多次运行（与 Model.Api 的 ClearAsync 同款）。
    /// </summary>
    public async Task ClearAsync()
    {
        var db = Database();
        foreach (var name in Collections)
            await db.GetCollection<BsonDocument>(name).DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
    }

    public Task InitializeAsync()
    {
        _runner = MongoDbRunner.Start();
        ConnectionString = _runner.ConnectionString;
        DatabaseName = "report_dashboard_" + Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _runner?.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("ReportDashboard")]
public class ReportDashboardCollection : ICollectionFixture<ReportDashboardFixture>
{
}
