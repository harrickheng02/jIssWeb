using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ForumReportEvidenceTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumReportEvidenceTests(ForumMeIntegrationFixture fx) => _fx = fx;

    private static HttpRequestMessage AuthReq(HttpMethod method, string url, string? json, string sub, string role)
    {
        var req = new HttpRequestMessage(method, url);
        if (json is not null)
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, role));
        return req;
    }

    private static HttpRequestMessage AuthGet(string url, string sub, string role) =>
        AuthReq(HttpMethod.Get, url, null, sub, role);

    private async Task<string> SeedPendingReportAsync(string suffix, string boardId = "general", string postId = "me-post-a")
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var reports = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var id = "ev-report-" + suffix;
        var now = DateTime.UtcNow;
        await reports.InsertOneAsync(new ForumReportRecord
        {
            Id = id,
            ReporterSub = "reporter-" + suffix,
            TargetType = "post",
            TargetId = postId,
            PostId = postId,
            BoardId = boardId,
            BoardTitle = boardId == "tech" ? "技术" : "综合",
            Reason = "test reason " + suffix,
            Status = ForumReportStatuses.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        return id;
    }

    private IMongoCollection<ForumReportEvidenceSnapshotRecord> Snapshots()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        return mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumReportEvidenceSnapshotRecord>(ForumMongoSetup.ReportEvidenceSnapshotsCollectionName);
    }

    private IMongoCollection<ForumModerationAuditRecord> Audit()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        return mongo.GetDatabase(_fx.DatabaseName)
            .GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
    }

    [Fact]
    public async Task Close_resolved_writes_evidence_snapshot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);

        var close = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(close)).StatusCode);

        var snap = await Snapshots().Find(x => x.ReportId == reportId).FirstOrDefaultAsync();
        Assert.NotNull(snap);
        Assert.Equal("test reason " + suffix, snap!.Report.Reason);
        Assert.False(snap.Target.Tombstone);
        Assert.Equal("me-post-a", snap.Target.TargetId);
    }

    [Fact]
    public async Task Acknowledge_does_not_write_snapshot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);

        var ack = AuthReq(HttpMethod.Post, $"/api/mod/reports/{reportId}/acknowledge", null,
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(ack)).StatusCode);

        var count = await Snapshots().CountDocumentsAsync(x => x.ReportId == reportId);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Duplicate_close_is_idempotent_for_snapshot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);
        var close = AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"rejected\"}",
            "user-admin", ForumRoleClaim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(close)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"rejected\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        var count = await Snapshots().CountDocumentsAsync(x => x.ReportId == reportId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Reopen_and_close_writes_new_snapshot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"pending\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"rejected\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        var count = await Snapshots().CountDocumentsAsync(x => x.ReportId == reportId);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Pending_export_returns_400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);

        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{reportId}/evidence", "user-admin", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("REPORT_NOT_CLOSED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_exports_closed_report_zip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{reportId}/evidence", "user-admin", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/zip", res.Content.Headers.ContentType?.MediaType);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("report.json"));
        Assert.NotNull(zip.GetEntry("target.json"));
        Assert.NotNull(zip.GetEntry("thread-audit.json"));
        Assert.NotNull(zip.GetEntry("sanctions-summary.json"));
        using (var readme = zip.GetEntry("readme.txt")!.Open())
        using (var reader = new StreamReader(readme))
        {
            var text = await reader.ReadToEndAsync();
            Assert.Contains("【用途】", text);
            Assert.Contains("运营复盘级", text);
            Assert.Contains("thread-audit.json", text);
        }
    }

    [Fact]
    public async Task Export_after_report_purge_uses_snapshot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var reports = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
            await reports.DeleteOneAsync(x => x.Id == reportId);
        }

        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{reportId}/evidence", "user-admin", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Moderator_out_of_scope_export_returns_403()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix, boardId: "tech", postId: "me-post-tech");
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{reportId}/evidence", "user-mod", ForumRoleClaim.Moderator));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Purge_removes_snapshots_and_reports_keeps_audit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = "ev-purge-" + suffix;
        var handledAt = DateTime.UtcNow.AddDays(-200);
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var db = mongo.GetDatabase(_fx.DatabaseName);
            await db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName).InsertOneAsync(new ForumReportRecord
            {
                Id = reportId,
                ReporterSub = "rep-" + suffix,
                TargetType = "post",
                TargetId = "me-post-a",
                PostId = "me-post-a",
                BoardId = "general",
                BoardTitle = "综合",
                Status = ForumReportStatuses.Resolved,
                CreatedAtUtc = handledAt,
                UpdatedAtUtc = handledAt,
                HandledBySub = "mod",
                HandledAtUtc = handledAt,
            });
            await db.GetCollection<ForumReportEvidenceSnapshotRecord>(ForumMongoSetup.ReportEvidenceSnapshotsCollectionName)
                .InsertOneAsync(new ForumReportEvidenceSnapshotRecord
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    ReportId = reportId,
                    BoardId = "general",
                    HandledAtUtc = handledAt,
                    SnapshottedAtUtc = handledAt,
                    Report = new ForumReportEvidenceReportSnapshot { Id = reportId, Status = ForumReportStatuses.Resolved },
                    Target = new ForumReportEvidenceTargetSnapshot { TargetType = "post", TargetId = "me-post-a" },
                });
            await db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName)
                .InsertOneAsync(new ForumModerationAuditRecord
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    TargetType = "report",
                    TargetId = reportId,
                    Action = "report.resolve",
                    OperatorSub = "mod",
                    OccurredAtUtc = handledAt,
                    Metadata = new Dictionary<string, object> { ["reportId"] = reportId, ["postId"] = "me-post-a" },
                });
        }

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var db = mongo.GetDatabase(_fx.DatabaseName);
            var reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
            var snapshots = db.GetCollection<ForumReportEvidenceSnapshotRecord>(ForumMongoSetup.ReportEvidenceSnapshotsCollectionName);
            var now = DateTime.UtcNow;
            await ForumReportRetentionPurger.PurgeStaleClosedAsync(reports, 120, now);
            await ForumReportRetentionPurger.PurgeStaleEvidenceSnapshotsAsync(snapshots, 120, now);
        }

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            var db = mongo.GetDatabase(_fx.DatabaseName);
            var reportExists = await db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName)
                .Find(x => x.Id == reportId).AnyAsync();
            var snapExists = await db.GetCollection<ForumReportEvidenceSnapshotRecord>(ForumMongoSetup.ReportEvidenceSnapshotsCollectionName)
                .Find(x => x.ReportId == reportId).AnyAsync();
            var auditExists = await db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName)
                .Find(x => x.TargetId == reportId).AnyAsync();
            Assert.False(reportExists);
            Assert.False(snapExists);
            Assert.True(auditExists);
        }

        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{reportId}/evidence", "user-admin", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("EVIDENCE_EXPIRED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sanctions_summary_in_zip_from_audit_only()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);
        await Audit().InsertOneAsync(new ForumModerationAuditRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TargetType = "user",
            TargetId = "author-sub",
            Action = "user.mute",
            OperatorSub = "user-admin",
            OccurredAtUtc = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["reportId"] = reportId,
                ["postId"] = "me-post-a",
                ["reason"] = "spam mute",
                ["durationPreset"] = "24h",
            },
        });
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{reportId}/evidence", "user-admin", ForumRoleClaim.Admin));
        var bytes = await res.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("sanctions-summary.json")!.Open();
        using var reader = new StreamReader(entry);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
        var first = doc.RootElement[0];
        Assert.Equal("user.mute", first.GetProperty("action").GetString());
        Assert.Equal("spam mute", first.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Sanctions_summary_excludes_other_report_on_same_post()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);
        await Audit().InsertOneAsync(new ForumModerationAuditRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TargetType = "user",
            TargetId = "other-author",
            Action = "user.mute",
            OperatorSub = "user-admin",
            OccurredAtUtc = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["reportId"] = "other-report-" + suffix,
                ["postId"] = "me-post-a",
                ["reason"] = "other case",
                ["durationPreset"] = "7d",
            },
        });
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{reportId}/evidence", "user-admin", ForumRoleClaim.Admin));
        var bytes = await res.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("sanctions-summary.json")!.Open();
        using var reader = new StreamReader(entry);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Unknown_report_export_returns_REPORT_NOT_FOUND()
    {
        var missingId = "missing-report-" + Guid.NewGuid().ToString("N")[..8];
        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{missingId}/evidence", "user-admin", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("REPORT_NOT_FOUND", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Export_backfills_missing_snapshot_on_closed_report()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportId = await SeedPendingReportAsync(suffix);
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(AuthReq(HttpMethod.Patch, $"/api/mod/reports/{reportId}", "{\"status\":\"resolved\"}",
            "user-admin", ForumRoleClaim.Admin))).StatusCode);

        await Snapshots().DeleteManyAsync(x => x.ReportId == reportId);

        var res = await _fx.Client.SendAsync(AuthGet($"/api/mod/reports/{reportId}/evidence", "user-admin", ForumRoleClaim.Admin));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var snap = await Snapshots().Find(x => x.ReportId == reportId).FirstOrDefaultAsync();
        Assert.NotNull(snap);
        Assert.False(snap!.Target.Tombstone);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var targetEntry = zip.GetEntry("target.json")!.Open();
        using var targetReader = new StreamReader(targetEntry);
        using var targetDoc = JsonDocument.Parse(await targetReader.ReadToEndAsync());
        Assert.False(targetDoc.RootElement.GetProperty("tombstone").GetBoolean());
    }
}
