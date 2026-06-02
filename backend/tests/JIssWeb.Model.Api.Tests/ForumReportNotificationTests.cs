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

[Collection("ForumReportNotification")]
public class ForumReportNotificationTests
{
    private readonly ForumReportNotificationIntegrationFixture _fx;

    public ForumReportNotificationTests(ForumReportNotificationIntegrationFixture fx) => _fx = fx;

    // ── helpers ──────────────────────────────────────────────────────────────

    private static HttpRequestMessage Req(HttpMethod method, string url, string json) =>
        new(method, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpRequestMessage AuthReq(HttpMethod method, string url, string json, string sub, string role) =>
        new(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, role)) },
        };

    private static HttpRequestMessage AuthPost(string url, string sub, string role) =>
        new(HttpMethod.Post, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, role)) },
        };

    /// <summary>Inserts a pending report and returns its Id.</summary>
    private async Task<string> SeedReportAsync(string reporterSub, string postId = "rn-post-1")
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var id = ObjectId.GenerateNewId().ToString();
        await reports.InsertOneAsync(new ForumReportRecord
        {
            Id = id,
            ReporterSub = reporterSub,
            TargetType = "post",
            TargetId = postId,
            PostId = postId,
            BoardId = "general",
            BoardTitle = "综合",
            Status = ForumReportStatuses.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        return id;
    }

    private IMongoCollection<InAppNotificationRecord> Notifications()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        return db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
    }

    // ── 4.1: resolved triggers notification ──────────────────────────────────

    [Fact]
    public async Task Resolved_creates_ReportResolved_notification_for_reporter()
    {
        var reporterSub = "reporter-resolved-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var col = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var notif = await col.Find(x => x.ReportId == reportId).FirstOrDefaultAsync();

        Assert.NotNull(notif);
        Assert.Equal(InAppNotificationTypes.ReportResolved, notif!.Type);
        Assert.Equal(reporterSub, notif.RecipientSubId);
        Assert.Equal(reportId, notif.ReportId);
        Assert.Equal("rn-post-1", notif.PostId);
        Assert.Equal("举报通知测试帖", notif.PostTitle);
        Assert.Equal("", notif.ActorSubId);
    }

    // ── 4.2: rejected triggers notification ──────────────────────────────────

    [Fact]
    public async Task Rejected_creates_ReportResolved_notification_for_reporter()
    {
        var reporterSub = "reporter-rejected-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"rejected\"}",
            "user-admin", ForumRoleClaim.Admin);
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var col = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var notif = await col.Find(x => x.ReportId == reportId).FirstOrDefaultAsync();

        Assert.NotNull(notif);
        Assert.Equal(InAppNotificationTypes.ReportResolved, notif!.Type);
        Assert.Equal(reporterSub, notif.RecipientSubId);
    }

    // ── 4.2b: alias status values (acknowledged / dismissed) trigger notification

    [Fact]
    public async Task Acknowledged_alias_creates_ReportResolved_notification_for_reporter()
    {
        var reporterSub = "reporter-acknowledged-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"acknowledged\"}",
            "user-admin", ForumRoleClaim.Admin);
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var col = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var notif = await col.Find(x => x.ReportId == reportId).FirstOrDefaultAsync();

        Assert.NotNull(notif);
        Assert.Equal(InAppNotificationTypes.ReportResolved, notif!.Type);
        Assert.Equal(reporterSub, notif.RecipientSubId);
    }

    [Fact]
    public async Task Dismissed_alias_creates_ReportResolved_notification_for_reporter()
    {
        var reporterSub = "reporter-dismissed-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"dismissed\"}",
            "user-admin", ForumRoleClaim.Admin);
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var col = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var notif = await col.Find(x => x.ReportId == reportId).FirstOrDefaultAsync();

        Assert.NotNull(notif);
        Assert.Equal(InAppNotificationTypes.ReportResolved, notif!.Type);
        Assert.Equal(reporterSub, notif.RecipientSubId);
    }

    // ── 4.3: idempotency — re-closing doesn't produce a second notification ──

    [Fact]
    public async Task Closing_twice_produces_only_one_notification_record()
    {
        var reporterSub = "reporter-idempotent-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var authHeader = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin));

        // First close: resolved
        var req1 = new HttpRequestMessage(HttpMethod.Patch, $"/api/mod/reports/{reportId}")
        {
            Content = new StringContent("{\"status\":\"resolved\"}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = authHeader },
        };
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(req1)).StatusCode);

        // Reopen
        var req2 = new HttpRequestMessage(HttpMethod.Patch, $"/api/mod/reports/{reportId}")
        {
            Content = new StringContent("{\"status\":\"pending\"}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = authHeader },
        };
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(req2)).StatusCode);

        // Close again: rejected — the sparse unique index on ReportId prevents a second notification
        var req3 = new HttpRequestMessage(HttpMethod.Patch, $"/api/mod/reports/{reportId}")
        {
            Content = new StringContent("{\"status\":\"rejected\"}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = authHeader },
        };
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(req3)).StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var col = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var count = await col.CountDocumentsAsync(x => x.ReportId == reportId);
        Assert.Equal(1, count);
    }

    // ── 4.4: pending (reopen) produces no notification ────────────────────────

    [Fact]
    public async Task Pending_reopen_does_not_create_notification()
    {
        var reporterSub = "reporter-pending-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var authHeader = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin));

        // Close first so we can reopen
        var close = new HttpRequestMessage(HttpMethod.Patch, $"/api/mod/reports/{reportId}")
        {
            Content = new StringContent("{\"status\":\"resolved\"}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = authHeader },
        };
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(close)).StatusCode);

        // Count notifications before reopen
        using var scope1 = _fx.Factory.Services.CreateScope();
        var mongo1 = scope1.ServiceProvider.GetRequiredService<IMongoClient>();
        var col1 = mongo1.GetDatabase(_fx.DatabaseName)
            .GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var countBefore = await col1.CountDocumentsAsync(x => x.ReportId == reportId);

        // Reopen to pending
        var reopen = new HttpRequestMessage(HttpMethod.Patch, $"/api/mod/reports/{reportId}")
        {
            Content = new StringContent("{\"status\":\"pending\"}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = authHeader },
        };
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(reopen)).StatusCode);

        // Count after reopen — must not have increased
        using var scope2 = _fx.Factory.Services.CreateScope();
        var mongo2 = scope2.ServiceProvider.GetRequiredService<IMongoClient>();
        var col2 = mongo2.GetDatabase(_fx.DatabaseName)
            .GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var countAfter = await col2.CountDocumentsAsync(x => x.ReportId == reportId);

        Assert.Equal(countBefore, countAfter);
    }

    // ── 4.5: post deleted — PostTitle stored as empty string, write succeeds ─

    [Fact]
    public async Task Resolved_with_missing_post_stores_empty_PostTitle()
    {
        var reporterSub = "reporter-nopost-" + Guid.NewGuid().ToString("N");

        // Seed report pointing to a non-existent post
        using var seedScope = _fx.Factory.Services.CreateScope();
        var seedMongo = seedScope.ServiceProvider.GetRequiredService<IMongoClient>();
        var seedDb = seedMongo.GetDatabase(_fx.DatabaseName);
        var reports = seedDb.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var reportId = ObjectId.GenerateNewId().ToString();
        await reports.InsertOneAsync(new ForumReportRecord
        {
            Id = reportId,
            ReporterSub = reporterSub,
            TargetType = "post",
            TargetId = "ghost-post-does-not-exist",
            PostId = "ghost-post-does-not-exist",
            BoardId = "general",
            BoardTitle = "综合",
            Status = ForumReportStatuses.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var req = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var col = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var notif = await col.Find(x => x.ReportId == reportId).FirstOrDefaultAsync();

        Assert.NotNull(notif);
        Assert.Equal("", notif!.PostTitle);
        Assert.Equal(InAppNotificationTypes.ReportResolved, notif.Type);
    }

    // ── DTO mapping: actorDisplayName is "系统", actorId is empty ────────────

    [Fact]
    public async Task Notification_list_returns_ReportResolved_with_system_actor_display_name()
    {
        var reporterSub = "reporter-dto-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var closeReq = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(closeReq)).StatusCode);

        var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/forum/notifications?page=1&pageSize=50");
        listReq.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken(reporterSub, ForumRoleClaim.Member));
        var listRes = await _fx.Client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        using var doc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        var notifEl = items.EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("type").GetString() == InAppNotificationTypes.ReportResolved);

        Assert.NotEqual(default, notifEl);
        Assert.Equal("系统", notifEl.GetProperty("actorDisplayName").GetString());
        Assert.Equal("", notifEl.GetProperty("actorId").GetString());
        Assert.Equal("举报通知测试帖", notifEl.GetProperty("postTitle").GetString());
        Assert.Equal("rn-post-1", notifEl.GetProperty("postId").GetString());
    }

    [Fact]
    public async Task Acknowledge_creates_ReportAcknowledged_notification_for_reporter()
    {
        var reporterSub = "reporter-ack-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var col = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var notif = await col.Find(x => x.ReportId == reportId && x.Type == InAppNotificationTypes.ReportAcknowledged)
            .FirstOrDefaultAsync();

        Assert.NotNull(notif);
        Assert.Equal(reporterSub, notif!.RecipientSubId);
        Assert.Equal("举报通知测试帖", notif.PostTitle);
        Assert.Equal("", notif.ActorSubId);
    }

    [Fact]
    public async Task Acknowledge_twice_produces_only_one_ReportAcknowledged_notification()
    {
        var reporterSub = "reporter-ack-idem-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);
        var auth = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);

        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(auth)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin))).StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var col = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var count = await col.CountDocumentsAsync(x =>
            x.ReportId == reportId && x.Type == InAppNotificationTypes.ReportAcknowledged);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Acknowledge_on_closed_report_returns_400()
    {
        var reporterSub = "reporter-ack-closed-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var close = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(close)).StatusCode);

        var ack = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);
        var res = await _fx.Client.SendAsync(ack);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Acknowledged_then_closed_produces_two_notifications()
    {
        var reporterSub = "reporter-two-node-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var ack = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(ack)).StatusCode);

        var close = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(close)).StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var col = mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var ackNotif = await col.Find(x => x.ReportId == reportId && x.Type == InAppNotificationTypes.ReportAcknowledged)
            .FirstOrDefaultAsync();
        var resolvedNotif = await col.Find(x => x.ReportId == reportId && x.Type == InAppNotificationTypes.ReportResolved)
            .FirstOrDefaultAsync();

        Assert.NotNull(ackNotif);
        Assert.NotNull(resolvedNotif);
    }

    [Fact]
    public async Task Acknowledge_keeps_status_pending_and_sets_acknowledge_fields()
    {
        var reporterSub = "reporter-ack-fields-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("pending", data.GetProperty("status").GetString());
        Assert.True(data.TryGetProperty("acknowledgedAtUtc", out var ackAt) && ackAt.ValueKind != JsonValueKind.Null);
        Assert.Equal("user-admin", data.GetProperty("acknowledgedBySub").GetString());
    }

    [Fact]
    public async Task Notification_list_returns_ReportAcknowledged_with_system_actor_display_name()
    {
        var reporterSub = "reporter-ack-dto-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var ackReq = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(ackReq)).StatusCode);

        var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/forum/notifications?page=1&pageSize=50");
        listReq.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken(reporterSub, ForumRoleClaim.Member));
        var listRes = await _fx.Client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        using var doc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        var notifEl = items.EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("type").GetString() == InAppNotificationTypes.ReportAcknowledged);

        Assert.NotEqual(default, notifEl);
        Assert.Equal("系统", notifEl.GetProperty("actorDisplayName").GetString());
        Assert.Equal("", notifEl.GetProperty("actorId").GetString());
    }

    // ── report workflow audit (Issue #22) ────────────────────────────────────

    private IMongoCollection<ForumModerationAuditRecord> Audit()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        return db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
    }

    [Fact]
    public async Task Patch_resolved_writes_report_resolve_audit_with_postId()
    {
        var reporterSub = "reporter-audit-res-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(req)).StatusCode);

        var row = await Audit().Find(x => x.TargetId == reportId && x.Action == "report.resolve").FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal("report", row!.TargetType);
        Assert.Equal("rn-post-1", row.Metadata!["postId"]?.ToString());
        Assert.Equal("general", row.Metadata["boardId"]?.ToString());
    }

    [Fact]
    public async Task Patch_rejected_writes_report_reject_audit()
    {
        var reporterSub = "reporter-audit-rej-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"rejected\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(req)).StatusCode);

        var count = await Audit().CountDocumentsAsync(x => x.TargetId == reportId && x.Action == "report.reject");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Patch_reopen_does_not_write_close_audit()
    {
        var reporterSub = "reporter-audit-reopen-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var close = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(close)).StatusCode);

        var reopen = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"pending\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(reopen)).StatusCode);

        var count = await Audit().CountDocumentsAsync(x => x.TargetId == reportId && x.Action == "report.resolve");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Patch_repeat_close_to_same_terminal_skips_duplicate_audit()
    {
        var reporterSub = "reporter-audit-idem-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(req)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        var count = await Audit().CountDocumentsAsync(x => x.TargetId == reportId && x.Action == "report.resolve");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Acknowledge_writes_report_acknowledge_audit()
    {
        var reporterSub = "reporter-audit-ack-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(req)).StatusCode);

        var row = await Audit().Find(x => x.TargetId == reportId && x.Action == "report.acknowledge").FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal("rn-post-1", row!.Metadata!["postId"]?.ToString());
    }

    [Fact]
    public async Task Acknowledge_audit_is_idempotent_on_repeat()
    {
        var reporterSub = "reporter-audit-ack2-" + Guid.NewGuid().ToString("N");
        var reportId = await SeedReportAsync(reporterSub);

        var req = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(req)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin))).StatusCode);

        var count = await Audit().CountDocumentsAsync(x => x.TargetId == reportId && x.Action == "report.acknowledge");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Post_audit_lists_report_acknowledge_after_acknowledge()
    {
        var reporterSub = "reporter-audit-post-" + Guid.NewGuid().ToString("N");
        var postId = "rn-post-1";
        var reportId = await SeedReportAsync(reporterSub, postId);

        var ack = AuthPost($"/api/mod/reports/{reportId}/acknowledge", "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(ack)).StatusCode);

        var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/mod/audit?targetType=post&targetId={postId}&action=report.acknowledge")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) },
        };
        var r = await _fx.Client.SendAsync(auditReq);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        Assert.Equal("标记举报已受理", items[0].GetProperty("actionLabel").GetString());
    }

    [Fact]
    public async Task Terminal_transition_rejected_then_resolved_writes_two_audit_rows_on_post()
    {
        var reporterSub = "reporter-audit-dual-" + Guid.NewGuid().ToString("N");
        var postId = "rn-post-1";
        var reportId = await SeedReportAsync(reporterSub, postId);

        var reject = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"rejected\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(reject)).StatusCode);

        var resolve = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(resolve)).StatusCode);

        var rejectCount = await Audit().CountDocumentsAsync(x => x.TargetId == reportId && x.Action == "report.reject");
        var resolveCount = await Audit().CountDocumentsAsync(x => x.TargetId == reportId && x.Action == "report.resolve");
        Assert.Equal(1, rejectCount);
        Assert.Equal(1, resolveCount);

        var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/mod/audit?targetType=post&targetId={postId}&page=1&pageSize=20")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) },
        };
        var r = await _fx.Client.SendAsync(auditReq);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var labels = doc.RootElement.GetProperty("data").GetProperty("items")
            .EnumerateArray()
            .Select(x => x.GetProperty("actionLabel").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("驳回举报", labels);
        Assert.Contains("结案举报", labels);
    }
}
