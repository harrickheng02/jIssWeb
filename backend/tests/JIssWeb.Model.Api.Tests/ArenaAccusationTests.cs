using System;
using System.Net;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

[Collection("ArenaAccusation")]
public class ArenaAccusationTests
{
    private readonly ArenaAccusationIntegrationFixture _fx;

    public ArenaAccusationTests(ArenaAccusationIntegrationFixture fx) => _fx = fx;

    /// <summary>
    /// 同一 Collection 共享 fixture 与数据库；每个测试前清空竞技域集合与通知集合，
    /// 使断言与执行顺序无关（修复跨测试计数累积）。
    /// </summary>
    private async Task ClearArenaAsync()
    {
        var db = _fx.GetDatabase();
        await db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName)
            .DeleteManyAsync(FilterDefinition<ArenaAccusationRecord>.Empty);
        await db.GetCollection<ArenaAccusationDedupeRecord>(ArenaMongoSetup.DedupeCollectionName)
            .DeleteManyAsync(FilterDefinition<ArenaAccusationDedupeRecord>.Empty);
        await db.GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName)
            .DeleteManyAsync(FilterDefinition<ArenaTargetTallyRecord>.Empty);
        await db.GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName)
            .DeleteManyAsync(FilterDefinition<ArenaPointsEventRecord>.Empty);
        await db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName)
            .DeleteManyAsync(FilterDefinition<InAppNotificationRecord>.Empty);
    }

    /// <summary>
    /// MongoDB TTL 索引的 expireAfterSeconds 在 BSON 中存为 double（如 604800.0），
    /// 也可能为 int；统一按数值比较，避免类型误判漏识别。
    /// </summary>
    private static bool IsTtlSeconds(BsonValue v, long seconds)
    {
        if (v.IsDouble) return Math.Abs(v.AsDouble - seconds) < 1;
        if (v.IsInt32) return v.AsInt32 == seconds;
        if (v.IsInt64) return v.AsInt64 == seconds;
        return false;
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAccusationAsync(
        HttpClient client, string? token, string? target)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/arena/accusations");
        if (token is not null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        object? body = target is null ? null : new { targetUserId = target };
        req.Content = body is null
            ? new StringContent("not-json", Encoding.UTF8, "application/json")
            : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await client.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 合法指认落_pending_与_dedupe_并_返回统一200()
    {
        await ClearArenaAsync();
        var token = JwtTestTokens.CreateAccessToken("accuser-1");
        var (status, body) = await SendAccusationAsync(_fx.Client, token, "target-1");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("{\"success\":true,\"data\":{},\"message\":null,\"code\":null}",
            body, ignoreCase: true);

        var db = _fx.GetDatabase();
        var accusations = db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);
        var dedupe = db.GetCollection<ArenaAccusationDedupeRecord>(ArenaMongoSetup.DedupeCollectionName);
        var tallies = db.GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName);
        var events = db.GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName);
        var notifications = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);

        Assert.Equal(1, await accusations.CountDocumentsAsync(FilterDefinition<ArenaAccusationRecord>.Empty));
        Assert.Equal(1, await dedupe.CountDocumentsAsync(FilterDefinition<ArenaAccusationDedupeRecord>.Empty));
        var tally = await tallies.Find(FilterDefinition<ArenaTargetTallyRecord>.Empty).FirstAsync();
        Assert.Equal(1, tally.Count);
        Assert.Equal("active", tally.Status);
        Assert.Equal(0, await events.CountDocumentsAsync(FilterDefinition<ArenaPointsEventRecord>.Empty));
        Assert.Equal(0, await notifications.CountDocumentsAsync(FilterDefinition<InAppNotificationRecord>.Empty));
    }

    [Fact]
    public async Task 重复指认7天内_字节级一致200_且只写一条记录()
    {
        await ClearArenaAsync();
        var token = JwtTestTokens.CreateAccessToken("accuser-2");
        var first = await SendAccusationAsync(_fx.Client, token, "target-2");
        var second = await SendAccusationAsync(_fx.Client, token, "target-2");

        // 字节级一致（含 headers 由框架统一，此处校验 body）。
        Assert.Equal(first.Body, second.Body);

        var db = _fx.GetDatabase();
        var accusations = db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);
        var dedupe = db.GetCollection<ArenaAccusationDedupeRecord>(ArenaMongoSetup.DedupeCollectionName);
        // 去重 11000 静默跳过：业务记录仍为 1 条。
        Assert.Equal(1, await accusations.CountDocumentsAsync(FilterDefinition<ArenaAccusationRecord>.Empty));
        Assert.Equal(1, await dedupe.CountDocumentsAsync(FilterDefinition<ArenaAccusationDedupeRecord>.Empty));
    }

    [Fact]
    public async Task 自指认_与_空目标_与_未鉴权_均返回字节级一致200且不写记录()
    {
        await ClearArenaAsync();
        var token = JwtTestTokens.CreateAccessToken("accuser-3");
        var target = await SendAccusationAsync(_fx.Client, token, "target-3");
        var self = await SendAccusationAsync(_fx.Client, token, "accuser-3");
        var empty = await SendAccusationAsync(_fx.Client, token, "");
        var unauth = await SendAccusationAsync(_fx.Client, null, "target-3");

        Assert.Equal(target.Body, self.Body);
        Assert.Equal(target.Body, empty.Body);
        Assert.Equal(target.Body, unauth.Body);

        var db = _fx.GetDatabase();
        var accusations = db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);
        // 仅第一次合法指认写入；其余分支应静默 no-op。
        Assert.Equal(1, await accusations.CountDocumentsAsync(FilterDefinition<ArenaAccusationRecord>.Empty));
    }

    [Fact]
    public async Task 三个不同指认者达到阈值_状态升为_threshold_reached()
    {
        await ClearArenaAsync();
        var t = "target-threshold";
        foreach (var u in new[] { "u-a", "u-b", "u-c" })
        {
            var (status, _) = await SendAccusationAsync(_fx.Client, JwtTestTokens.CreateAccessToken(u), t);
            Assert.Equal(HttpStatusCode.OK, status);
        }

        var db = _fx.GetDatabase();
        var tallies = db.GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName);
        var tally = await tallies.Find(FilterDefinition<ArenaTargetTallyRecord>.Empty).FirstAsync();
        Assert.Equal(3, tally.Count);
        Assert.Equal("threshold_reached", tally.Status);
    }

    [Fact]
    public async Task 指认时刻零积分事件_零通知_零跨服务判定()
    {
        await ClearArenaAsync();
        var (_, _) = await SendAccusationAsync(_fx.Client, JwtTestTokens.CreateAccessToken("accuser-z"), "target-z");

        var db = _fx.GetDatabase();
        var events = db.GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName);
        var notifications = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        Assert.Equal(0, await events.CountDocumentsAsync(FilterDefinition<ArenaPointsEventRecord>.Empty));
        Assert.Equal(0, await notifications.CountDocumentsAsync(FilterDefinition<InAppNotificationRecord>.Empty));
        // 写路径不写任何结算计划 / 不标记 agent：仅产生 pending 业务记录与 active 计数。
        var accusations = db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);
        var acc = await accusations.Find(FilterDefinition<ArenaAccusationRecord>.Empty).FirstAsync();
        Assert.Equal("pending", acc.Status);
    }

    [Fact]
    public async Task 生命周期解耦_P0_机制_dedupe有7天TTL_业务记录无TTL()
    {
        await ClearArenaAsync();
        var db = _fx.GetDatabase();
        var dedupe = db.GetCollection<ArenaAccusationDedupeRecord>(ArenaMongoSetup.DedupeCollectionName);
        var accusations = db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);

        var dedupeCursor = await dedupe.Indexes.ListAsync();
        var hasDedupeTtl = false;
        while (await dedupeCursor.MoveNextAsync())
        {
            foreach (var idx in dedupeCursor.Current)
            {
                if (idx.TryGetValue("expireAfterSeconds", out var v) && IsTtlSeconds(v, 604800))
                    hasDedupeTtl = true;
            }
        }
        Assert.True(hasDedupeTtl, "arena_accusation_dedupe 应存在 7 天 TTL 索引");

        var accCursor = await accusations.Indexes.ListAsync();
        while (await accCursor.MoveNextAsync())
        {
            foreach (var idx in accCursor.Current)
            {
                Assert.False(
                    idx.TryGetValue("expireAfterSeconds", out _),
                    "arena_accusations 不应存在任何 TTL 索引（否则周一指认会被提前删除导致漏结算）");
            }
        }
    }
}
