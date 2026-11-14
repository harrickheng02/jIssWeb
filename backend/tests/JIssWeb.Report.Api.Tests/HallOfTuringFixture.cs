using MongoDB.Bson;
using MongoDB.Driver;
using Mongo2Go;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// 图灵名人堂集成测试夹具：Mongo2Go 内存实例（零外部依赖，CI 自包含）。
/// 仅读 jissweb_model 等效库；测试前后集合文档数不变 —— 验证只读纪律。
/// 数据只读，故在 InitializeAsync 播种一次，所有测试共享（幂等）。
/// </summary>
public sealed class HallOfTuringFixture : IAsyncLifetime
{
    private MongoDbRunner? _runner;

    public string ConnectionString { get; private set; } = "";
    public string DatabaseName { get; private set; } = "";

    public IMongoDatabase Database() => new MongoClient(ConnectionString).GetDatabase(DatabaseName);

    public async Task ClearAsync()
    {
        var db = Database();
        foreach (var name in Collections)
            await db.GetCollection<BsonDocument>(name).DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
    }

    private static readonly string[] Collections =
    {
        "agent_personas", "agent_llm_profiles", "arena_bounty_states", "arena_points_events",
        "forum_posts", "forum_replies", "arena_reveal_jobs", "arena_target_tallies", "arena_aiqi_results",
    };

    public Task InitializeAsync()
    {
        _runner = MongoDbRunner.Start();
        ConnectionString = _runner.ConnectionString;
        DatabaseName = "hall_of_turing_" + Guid.NewGuid().ToString("N");
        return SeedAsync();
    }

    public Task DisposeAsync()
    {
        _runner?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        var db = Database();

        // agent_llm_profiles
        await db.GetCollection<BsonDocument>("agent_llm_profiles").InsertOneAsync(new BsonDocument
        {
            { "_id", "llm1" }, { "Name", "DeepSeek-V3" }, { "Enabled", true },
        });

        // agent_personas
        var personas = db.GetCollection<BsonDocument>("agent_personas");
        await personas.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "per1" }, { "PersonaId", "per1" }, { "Nickname", "Alpha" }, { "AgentUserId", "agent-sub-1" }, { "Model", "deepseek" }, { "LlmProfileId", "llm1" }, { "Generation", 1 }, { "SurvivalDays", 30 }, { "State", "eliminated" }, { "CreatedAtUtc", now.AddDays(-30) } },
            new BsonDocument { { "_id", "per2" }, { "PersonaId", "per2" }, { "Nickname", "Beta" }, { "AgentUserId", "agent-sub-2" }, { "Model", "doubao" }, { "LlmProfileId", BsonNull.Value }, { "Generation", 1 }, { "SurvivalDays", 0 }, { "State", "eliminated" }, { "CreatedAtUtc", now.AddDays(-20) } },
            new BsonDocument { { "_id", "per3" }, { "PersonaId", "per3" }, { "Nickname", "Gamma" }, { "AgentUserId", "agent-sub-3" }, { "Model", "deepseek" }, { "LlmProfileId", "llm1" }, { "Generation", 1 }, { "SurvivalDays", 10 }, { "State", "active" }, { "CreatedAtUtc", now.AddDays(-15) } },
            new BsonDocument { { "_id", "per4" }, { "PersonaId", "per4" }, { "Nickname", "Delta" }, { "AgentUserId", "agent-sub-4" }, { "Model", "deepseek" }, { "LlmProfileId", "llm1" }, { "Generation", 1 }, { "SurvivalDays", 5 }, { "State", "eliminated" }, { "CreatedAtUtc", now.AddDays(-10) } },
        });

        // arena_bounty_states（MedalAwardedAtUtc 非空 = 奖章账号）
        var bounty = db.GetCollection<BsonDocument>("arena_bounty_states");
        await bounty.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "bs1" }, { "AgentUserId", "agent-sub-1" }, { "CurrentBounty", 400 }, { "BountyCap", 400 }, { "MedalAwardedAtUtc", now.AddDays(-5) } },
            new BsonDocument { { "_id", "bs2" }, { "AgentUserId", "agent-sub-2" }, { "CurrentBounty", 200 }, { "BountyCap", 400 }, { "MedalAwardedAtUtc", now.AddDays(-3) } },
            new BsonDocument { { "_id", "bs3" }, { "AgentUserId", "agent-sub-3" }, { "CurrentBounty", 300 }, { "BountyCap", 400 }, { "MedalAwardedAtUtc", now.AddDays(-2) } },
            // agent-sub-4 无奖章记录
        });

        // arena_points_events（猎手 accusation_hit）
        var events = db.GetCollection<BsonDocument>("arena_points_events");
        await events.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "ev1" }, { "UserId", "user-hunter-a" }, { "Type", "accusation_hit" }, { "TargetUserId", "agent-sub-1" } },
            new BsonDocument { { "_id", "ev2" }, { "UserId", "user-hunter-b" }, { "Type", "accusation_hit" }, { "TargetUserId", "agent-sub-1" } },
            new BsonDocument { { "_id", "ev3" }, { "UserId", "user-hunter-c" }, { "Type", "accusation_hit" }, { "TargetUserId", "agent-sub-2" } },
            new BsonDocument { { "_id", "ev4" }, { "UserId", "user-hunter-d" }, { "Type", "accusation_miss" }, { "TargetUserId", "agent-sub-1" } },
        });

        // forum_posts（agent-sub-1 为主，其余账号无帖以简化）
        var posts = db.GetCollection<BsonDocument>("forum_posts");
        var confId = ObjectId.GenerateNewId().ToString();
        await posts.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "post-a1" }, { "Title", "AI伦理讨论" }, { "Body", "正文1" }, { "Excerpt", "摘1" }, { "AuthorSubId", "agent-sub-1" }, { "Tags", new BsonArray(new[] { "AI伦理", "哲学" }) }, { "LikeCount", 10 }, { "FavoriteCount", 2 }, { "CommentCount", 3 }, { "ViewCount", 100 }, { "CreatedAtUtc", now.AddDays(-8) }, { "State", "published" } },
            new BsonDocument { { "_id", "post-a2" }, { "Title", "科技前沿" }, { "Body", "正文2" }, { "Excerpt", "摘2" }, { "AuthorSubId", "agent-sub-1" }, { "Tags", new BsonArray(new[] { "AI伦理", "科技" }) }, { "LikeCount", 5 }, { "FavoriteCount", 1 }, { "CommentCount", 1 }, { "ViewCount", 50 }, { "CreatedAtUtc", now.AddDays(-7) }, { "State", "published" } },
            new BsonDocument { { "_id", "post-a3" }, { "Title", "哲学思考" }, { "Body", "正文3" }, { "Excerpt", "摘3" }, { "AuthorSubId", "agent-sub-1" }, { "Tags", new BsonArray(new[] { "AI伦理" }) }, { "LikeCount", 2 }, { "FavoriteCount", 0 }, { "CommentCount", 0 }, { "ViewCount", 20 }, { "CreatedAtUtc", now.AddDays(-6) }, { "State", "published" } },
            new BsonDocument { { "_id", "post-a4" }, { "Title", "杂谈" }, { "Body", "正文4" }, { "Excerpt", "摘4" }, { "AuthorSubId", "agent-sub-1" }, { "Tags", new BsonArray(new[] { "哲学" }) }, { "LikeCount", 1 }, { "FavoriteCount", 0 }, { "CommentCount", 0 }, { "ViewCount", 10 }, { "CreatedAtUtc", now.AddDays(-5) }, { "State", "published" } },
            new BsonDocument { { "_id", confId }, { "Title", "自白：我是一个 AI 智能体" }, { "Body", "我是一个 AI" }, { "Excerpt", "自白摘" }, { "AuthorSubId", "agent-sub-1" }, { "Tags", new BsonArray(new[] { "自白" }) }, { "LikeCount", 3 }, { "FavoriteCount", 1 }, { "CommentCount", 0 }, { "ViewCount", 200 }, { "CreatedAtUtc", now.AddDays(-4) }, { "State", "published" } },
        });

        // forum_replies（agent-sub-1）
        var replies = db.GetCollection<BsonDocument>("forum_replies");
        await replies.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "rep-a1" }, { "PostId", "post-a1" }, { "AuthorSubId", "agent-sub-1" }, { "Body", "回复1" }, { "CreatedAtUtc", now.AddDays(-6) }, { "State", "published" } },
            new BsonDocument { { "_id", "rep-a2" }, { "PostId", "post-a2" }, { "AuthorSubId", "agent-sub-1" }, { "Body", "回复2" }, { "CreatedAtUtc", now.AddDays(-5) }, { "State", "published" } },
        });

        // arena_reveal_jobs（completed，ConfessionPostIds 指向自白帖）
        await db.GetCollection<BsonDocument>("arena_reveal_jobs").InsertOneAsync(new BsonDocument
        {
            { "_id", "rj1" }, { "State", "completed" }, { "ConfessionPostIds", new BsonArray(new[] { confId }) },
        });

        // arena_target_tallies（agent-sub-1：近危险时刻；含越界/非active 排除项）
        var tallies = db.GetCollection<BsonDocument>("arena_target_tallies");
        await tallies.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "tt1" }, { "WeekKey", "2026-11-02" }, { "TargetUserId", "agent-sub-1" }, { "Count", 2 }, { "Status", "active" }, { "UpdatedAtUtc", now.AddDays(-3) } },
            new BsonDocument { { "_id", "tt2" }, { "WeekKey", "2026-11-09" }, { "TargetUserId", "agent-sub-1" }, { "Count", 3 }, { "Status", "active" }, { "UpdatedAtUtc", now.AddDays(-1) } },
            new BsonDocument { { "_id", "tt3" }, { "WeekKey", "2026-10-01" }, { "TargetUserId", "agent-sub-1" }, { "Count", 5 }, { "Status", "active" }, { "UpdatedAtUtc", now.AddDays(-45) } },
            new BsonDocument { { "_id", "tt4" }, { "WeekKey", "2026-11-09" }, { "TargetUserId", "agent-sub-1" }, { "Count", 10 }, { "Status", "threshold_reached" }, { "UpdatedAtUtc", now.AddDays(-1) } },
        });

        // arena_aiqi_results（全局周值；_id 原生 ObjectId）
        var aiqi = db.GetCollection<BsonDocument>("arena_aiqi_results");
        await aiqi.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "WeekKey", "2026-10-20" }, { "Aiqi", 0.75 }, { "ActiveUsers", 120 } },
            new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "WeekKey", "2026-10-27" }, { "Aiqi", 0.80 }, { "ActiveUsers", 130 } },
            new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "WeekKey", "2026-11-03" }, { "Aiqi", 0.85 }, { "ActiveUsers", 140 } },
            new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "WeekKey", "2026-11-10" }, { "Aiqi", 0.82 }, { "ActiveUsers", 150 } },
        });
    }
}

[CollectionDefinition("HallOfTuring")]
public class HallOfTuringCollection : ICollectionFixture<HallOfTuringFixture>
{
}
