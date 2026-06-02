using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

public class ForumModerationAuditFeedTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumModerationAuditFeedTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private static HttpRequestMessage FeedGet(
        string query,
        string role = ForumRoleClaim.Admin,
        string sub = "user-admin",
        IReadOnlyList<string>? boardIds = null)
    {
        var token = boardIds is { Count: > 0 }
            ? JwtTestTokens.CreateAccessToken(sub, role, boardIds)
            : JwtTestTokens.CreateAccessToken(sub, role);
        return new(HttpMethod.Get, $"/api/mod/audit/feed?{query}")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
    }

    private static HttpRequestMessage ExportGet(
        string query,
        string role = ForumRoleClaim.Admin,
        string sub = "user-admin",
        IReadOnlyList<string>? boardIds = null)
    {
        var token = boardIds is { Count: > 0 }
            ? JwtTestTokens.CreateAccessToken(sub, role, boardIds)
            : JwtTestTokens.CreateAccessToken(sub, role);
        return new(HttpMethod.Get, $"/api/mod/audit/export?{query}")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
    }

    private async Task SeedFeedRowsAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var audit = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        await audit.DeleteManyAsync(FilterDefinition<ForumModerationAuditRecord>.Empty);

        var now = DateTime.UtcNow;
        await audit.InsertManyAsync(new[]
        {
            new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = "feed-post-general",
                Action = "post.setSticky",
                OperatorSub = "user-admin",
                OccurredAtUtc = now.AddHours(-1),
                Metadata = new Dictionary<string, object> { ["boardId"] = "general", ["board"] = "综合" },
            },
            new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = "feed-post-tech",
                Action = "post.setFeatured",
                OperatorSub = "user-admin",
                OccurredAtUtc = now.AddHours(-2),
                Metadata = new Dictionary<string, object> { ["boardId"] = "tech", ["board"] = "技术" },
            },
            new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = "feed-post-legacy",
                Action = "post.lockReplies",
                OperatorSub = "user-admin",
                OccurredAtUtc = now.AddHours(-3),
                Metadata = new Dictionary<string, object>(),
            },
        });
    }

    [Fact]
    public async Task Feed_admin_sees_all_boards_including_missing_boardId()
    {
        await SeedFeedRowsAsync();
        var r = await _fx.Client.SendAsync(FeedGet("page=1&pageSize=50"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var total = doc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32();
        Assert.True(total >= 3);
    }

    [Fact]
    public async Task Feed_moderator_excludes_rows_without_boardId()
    {
        await SeedFeedRowsAsync();
        var r = await _fx.Client.SendAsync(FeedGet("page=1&pageSize=50", ForumRoleClaim.Moderator, "user-mod", new[] { "general" }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("general", items[0].GetProperty("boardId").GetString());
    }

    [Fact]
    public async Task Feed_moderator_out_of_scope_board_returns_403()
    {
        await SeedFeedRowsAsync();
        var r = await _fx.Client.SendAsync(
            FeedGet("page=1&pageSize=20&boardId=tech", ForumRoleClaim.Moderator, "user-mod", new[] { "general" }));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("FORBIDDEN", body);
    }

    [Fact]
    public async Task Feed_filter_by_time_range_excludes_out_of_range_rows()
    {
        await SeedFeedRowsAsync();
        var from = DateTime.UtcNow.AddHours(-1.5).ToString("O");
        var to = DateTime.UtcNow.AddMinutes(-30).ToString("O");
        var url = $"page=1&pageSize=50&fromUtc={Uri.EscapeDataString(from)}&toUtc={Uri.EscapeDataString(to)}";
        var r = await _fx.Client.SendAsync(FeedGet(url));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var total = doc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32();
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Feed_member_returns_403()
    {
        var r = await _fx.Client.SendAsync(FeedGet("page=1&pageSize=20", ForumRoleClaim.Member, "user-a"));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("FORBIDDEN", body);
    }

    [Fact]
    public async Task Feed_filter_by_action()
    {
        await SeedFeedRowsAsync();
        var r = await _fx.Client.SendAsync(FeedGet("page=1&pageSize=50&action=post.setSticky"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        foreach (var item in items.EnumerateArray())
            Assert.Equal("置顶帖子", item.GetProperty("actionLabel").GetString());
    }

    [Fact]
    public async Task Feed_excludes_audit_export_when_action_unfiltered()
    {
        await SeedFeedRowsAsync();
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var audit = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        await audit.InsertOneAsync(new ForumModerationAuditRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TargetType = "system",
            TargetId = "export-meta",
            Action = "audit.export",
            OperatorSub = "user-admin",
            OccurredAtUtc = DateTime.UtcNow,
            Metadata = new Dictionary<string, object> { ["exportedCount"] = 1 },
        });

        var r = await _fx.Client.SendAsync(FeedGet("page=1&pageSize=50"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        foreach (var item in items.EnumerateArray())
            Assert.NotEqual("导出审计记录", item.GetProperty("actionLabel").GetString());
    }

    [Fact]
    public async Task Export_csv_ordered_by_occurredAtUtc_descending()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var audit = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        await audit.DeleteManyAsync(FilterDefinition<ForumModerationAuditRecord>.Empty);

        var now = DateTime.UtcNow;
        await audit.InsertManyAsync(new[]
        {
            new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = "older-post",
                Action = "post.setSticky",
                OperatorSub = "user-admin",
                OccurredAtUtc = now.AddHours(-2),
                Metadata = new Dictionary<string, object> { ["boardId"] = "general" },
            },
            new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = "newer-post",
                Action = "post.setSticky",
                OperatorSub = "user-admin",
                OccurredAtUtc = now.AddHours(-1),
                Metadata = new Dictionary<string, object> { ["boardId"] = "general" },
            },
        });

        var r = await _fx.Client.SendAsync(ExportGet("action=post.setSticky"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var csv = Encoding.UTF8.GetString(await r.Content.ReadAsByteArrayAsync()).TrimStart('\uFEFF');
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(lines.Length >= 3);
        var firstData = lines[1];
        var secondData = lines[2];
        Assert.Contains("newer-post", firstData);
        Assert.Contains("older-post", secondData);
    }

    [Fact]
    public async Task Export_moderator_respects_board_scope()
    {
        await SeedFeedRowsAsync();
        var r = await _fx.Client.SendAsync(
            ExportGet("action=post.setSticky", ForumRoleClaim.Moderator, "user-mod", new[] { "general" }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var csv = Encoding.UTF8.GetString(await r.Content.ReadAsByteArrayAsync()).TrimStart('\uFEFF');
        Assert.Contains("feed-post-general", csv);
        Assert.DoesNotContain("feed-post-tech", csv);
        Assert.DoesNotContain("加精", csv);
    }

    [Fact]
    public async Task Export_csv_has_header_and_rows()
    {
        await SeedFeedRowsAsync();
        var r = await _fx.Client.SendAsync(ExportGet("page=1&pageSize=50"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("text/csv", r.Content.Headers.ContentType?.ToString());
        var csv = Encoding.UTF8.GetString(await r.Content.ReadAsByteArrayAsync()).TrimStart('\uFEFF');
        Assert.StartsWith("occurredAtUtc,actionLabel", csv, StringComparison.Ordinal);
        Assert.Contains("置顶帖子", csv);
    }

    [Fact]
    public async Task Export_writes_audit_export_row()
    {
        await SeedFeedRowsAsync();
        var before = await CountAuditExportRowsAsync();
        var r = await _fx.Client.SendAsync(ExportGet("action=post.setSticky"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var after = await CountAuditExportRowsAsync();
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task Export_too_large_returns_400()
    {
        await SeedManyRowsAsync(5);

        var factory = _fx.Factory.WithWebHostBuilder(b => b.UseSetting("Forum:ModerationAudit:MaxExportRows", "1"));
        using var client = factory.CreateClient();
        var req = ExportGet("page=1&pageSize=50");
        var r = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("EXPORT_TOO_LARGE", body);
    }

    private async Task SeedManyRowsAsync(int count)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var audit = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(0, count).Select(i => new ForumModerationAuditRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TargetType = "post",
            TargetId = $"bulk-{i}",
            Action = "post.setSticky",
            OperatorSub = "user-admin",
            OccurredAtUtc = now.AddMinutes(-i),
            Metadata = new Dictionary<string, object> { ["boardId"] = "general" },
        });
        await audit.InsertManyAsync(rows);
    }

    private async Task<long> CountAuditExportRowsAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var audit = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        return await audit.CountDocumentsAsync(x => x.Action == "audit.export");
    }
}
