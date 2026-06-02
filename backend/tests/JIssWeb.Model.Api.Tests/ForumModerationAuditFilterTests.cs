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

public class ForumModerationAuditFilterTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumModerationAuditFilterTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private static HttpRequestMessage AuditGet(string url, string role = ForumRoleClaim.Admin, string sub = "user-admin") =>
        new(HttpMethod.Get, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, role)) },
        };

    private async Task SeedAuditRowsAsync(string postId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);

        var t0 = DateTime.UtcNow.AddDays(-2);
        var t1 = DateTime.UtcNow.AddDays(-1);
        var t2 = DateTime.UtcNow;

        await audit.InsertManyAsync(new[]
        {
            new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = postId,
                Action = "post.setSticky",
                OperatorSub = "user-admin",
                OccurredAtUtc = t0,
                Metadata = new Dictionary<string, object> { ["boardId"] = "general" },
            },
            new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = postId,
                Action = "post.setFeatured",
                OperatorSub = "user-admin",
                OccurredAtUtc = t1,
                Metadata = new Dictionary<string, object> { ["boardId"] = "general" },
            },
            new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "user",
                TargetId = "user-author",
                Action = "user.warn",
                OperatorSub = "user-admin",
                OccurredAtUtc = t2,
                Metadata = new Dictionary<string, object> { ["postId"] = postId, ["reportId"] = "r-test" },
            },
        });
    }

    [Fact]
    public async Task Audit_filter_by_action_returns_matching_rows_only()
    {
        const string postId = "me-post-a";
        await SeedAuditRowsAsync(postId);

        var url = $"/api/mod/audit?targetType=post&targetId={postId}&page=1&pageSize=20&action=post.setSticky";
        var r = await _fx.Client.SendAsync(AuditGet(url));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("置顶帖子", items[0].GetProperty("actionLabel").GetString());
    }

    [Fact]
    public async Task Audit_filter_by_time_range_excludes_out_of_range_rows()
    {
        const string postId = "me-post-b";
        await SeedAuditRowsAsync(postId);

        var from = DateTime.UtcNow.AddDays(-1.5).ToString("O");
        var to = DateTime.UtcNow.AddHours(-1).ToString("O");
        var url = $"/api/mod/audit?targetType=post&targetId={postId}&page=1&pageSize=20&fromUtc={Uri.EscapeDataString(from)}&toUtc={Uri.EscapeDataString(to)}";
        var r = await _fx.Client.SendAsync(AuditGet(url));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var total = doc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32();
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Audit_invalid_time_range_returns_400()
    {
        var from = DateTime.UtcNow.ToString("O");
        var to = DateTime.UtcNow.AddDays(-1).ToString("O");
        var url = $"/api/mod/audit?targetType=post&targetId=me-post-a&fromUtc={Uri.EscapeDataString(from)}&toUtc={Uri.EscapeDataString(to)}";
        var r = await _fx.Client.SendAsync(AuditGet(url));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_TIME_RANGE", body);
    }

    [Fact]
    public async Task Audit_unknown_action_returns_400()
    {
        var url = "/api/mod/audit?targetType=post&targetId=me-post-a&action=not.a.real.action";
        var r = await _fx.Client.SendAsync(AuditGet(url));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_ACTION", body);
    }

    [Fact]
    public async Task Audit_pagination_returns_second_page()
    {
        const string postId = "me-post-a";
        await SeedAuditRowsAsync(postId);

        var url = $"/api/mod/audit?targetType=post&targetId={postId}&page=2&pageSize=1";
        var r = await _fx.Client.SendAsync(AuditGet(url));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("totalCount").GetInt32() >= 3);
        Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(1, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Post_thread_audit_includes_user_sanction_linked_by_postId()
    {
        var postId = "audit-user-warn-" + Guid.NewGuid().ToString("N");
        await SeedAuditRowsForPostAsync(postId);

        var url = $"/api/mod/audit?targetType=post&targetId={postId}&page=1&pageSize=20&action=user.warn";
        var r = await _fx.Client.SendAsync(AuditGet(url));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("账号警告", items[0].GetProperty("actionLabel").GetString());
    }

    private async Task SeedAuditRowsForPostAsync(string postId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);

        var t2 = DateTime.UtcNow;
        await audit.InsertOneAsync(new ForumModerationAuditRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TargetType = "user",
            TargetId = "user-author",
            Action = "user.warn",
            OperatorSub = "user-admin",
            OccurredAtUtc = t2,
            Metadata = new Dictionary<string, object> { ["postId"] = postId, ["reportId"] = "r-test", ["boardId"] = "general" },
        });
    }
}
