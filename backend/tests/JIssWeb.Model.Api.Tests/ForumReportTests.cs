using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public class ForumReportTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumReportTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task Submit_report_without_auth_returns_401()
    {
        var r = await _fx.Client.PostAsync(
            "/api/forum/reports",
            new StringContent("{\"targetType\":\"post\",\"targetId\":\"me-post-a\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Submit_report_member_success()
    {
        var req = Req(HttpMethod.Post, "/api/forum/reports", "{\"targetType\":\"post\",\"targetId\":\"me-post-a\",\"reason\":\"spam\"}");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Member));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("id").GetString()?.Length > 0);
    }

    [Fact]
    public async Task Submit_duplicate_pending_returns_409()
    {
        async Task<HttpResponseMessage> PostOnce()
        {
            var req = Req(HttpMethod.Post, "/api/forum/reports", "{\"targetType\":\"reply\",\"targetId\":\"me-reply-b\"}");
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                JwtTestTokens.CreateAccessToken("user-b", ForumRoleClaim.Member));
            return await _fx.Client.SendAsync(req);
        }

        var first = await PostOnce();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await PostOnce();
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var b = await second.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(b);
        Assert.Equal("DUPLICATE_PENDING_REPORT", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_report_unknown_post_returns_404()
    {
        var req = Req(HttpMethod.Post, "/api/forum/reports", "{\"targetType\":\"post\",\"targetId\":\"missing\"}");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Member));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Member_cannot_list_mod_reports()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/mod/reports?page=1&pageSize=20")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Member)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Admin_lists_reports_including_pending()
    {
        var sub = "report-list-" + Guid.NewGuid().ToString("N");
        var post = Req(HttpMethod.Post, "/api/forum/reports", "{\"targetType\":\"post\",\"targetId\":\"me-post-tech\"}");
        post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, ForumRoleClaim.Member));
        var pr = await _fx.Client.SendAsync(post);
        Assert.Equal(HttpStatusCode.OK, pr.StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/mod/reports?status=pending&page=1&pageSize=20")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin))
            }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        var anyTech = false;
        foreach (var el in items.EnumerateArray())
        {
            if (el.GetProperty("boardId").GetString() == "tech")
                anyTech = true;
        }
        Assert.True(anyTech);
    }

    [Fact]
    public async Task Moderator_list_excludes_out_of_scope_board()
    {
        var sub = "report-mod-scope-" + Guid.NewGuid().ToString("N");
        var post = Req(HttpMethod.Post, "/api/forum/reports", "{\"targetType\":\"post\",\"targetId\":\"me-post-tech\"}");
        post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, ForumRoleClaim.Member));
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(post)).StatusCode);

        post = Req(HttpMethod.Post, "/api/forum/reports", "{\"targetType\":\"post\",\"targetId\":\"me-post-a\"}");
        post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, ForumRoleClaim.Member));
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(post)).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/mod/reports?status=pending&page=1&pageSize=20")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator))
            }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        foreach (var el in doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray())
        {
            Assert.Equal("general", el.GetProperty("boardId").GetString());
        }
    }

    [Fact]
    public async Task Moderator_cannot_patch_tech_board_report()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        await reports.InsertOneAsync(new ForumReportRecord
        {
            Id = id,
            ReporterSub = "report-tech-patch-" + Guid.NewGuid().ToString("N"),
            TargetType = "post",
            TargetId = "me-post-tech",
            PostId = "me-post-tech",
            BoardId = "tech",
            BoardTitle = "技术",
            Status = ForumReportStatuses.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var req = Req(HttpMethod.Patch, $"/api/mod/reports/{id}", "{\"status\":\"rejected\"}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Patch_resolved_to_pending_then_to_rejected()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        await reports.InsertOneAsync(new ForumReportRecord
        {
            Id = id,
            ReporterSub = "report-reopen-" + Guid.NewGuid().ToString("N"),
            TargetType = "post",
            TargetId = "me-post-a",
            PostId = "me-post-a",
            BoardId = "general",
            BoardTitle = "综合",
            Status = ForumReportStatuses.Resolved,
            ResolutionCode = "resolved_logged",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            HandledBySub = "user-admin",
            HandledAtUtc = DateTime.UtcNow,
        });

        var auth = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin));

        var rPending = Req(HttpMethod.Patch, $"/api/mod/reports/{id}", "{\"status\":\"pending\"}");
        rPending.Headers.Authorization = auth;
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(rPending)).StatusCode);

        var row = await reports.Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(ForumReportStatuses.Pending, row!.Status);
        Assert.Null(row.HandledBySub);
        Assert.Null(row.HandledAtUtc);

        var rRej = Req(HttpMethod.Patch, $"/api/mod/reports/{id}", "{\"status\":\"rejected\"}");
        rRej.Headers.Authorization = auth;
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(rRej)).StatusCode);
        row = await reports.Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.Equal(ForumReportStatuses.Rejected, row!.Status);
        Assert.Equal("user-admin", row.HandledBySub);
        Assert.Null(row.ResolutionCode);
    }

    [Fact]
    public async Task Admin_patch_updates_status_and_clears_resolution_code_without_moderation_audit()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        await reports.InsertOneAsync(new ForumReportRecord
        {
            Id = id,
            ReporterSub = "report-audit-" + Guid.NewGuid().ToString("N"),
            TargetType = "post",
            TargetId = "me-post-b",
            PostId = "me-post-b",
            BoardId = "general",
            BoardTitle = "综合",
            Status = ForumReportStatuses.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var req = Req(HttpMethod.Patch, $"/api/mod/reports/{id}", "{\"status\":\"resolved\"}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin));
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        var found = await audit.Find(x => x.TargetType == "report" && x.TargetId == id && x.Action == "report.statusChange")
            .FirstOrDefaultAsync();
        Assert.Null(found);

        var row = await reports.Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(ForumReportStatuses.Resolved, row!.Status);
        Assert.Null(row.ResolutionCode);
        Assert.Equal("user-admin", row.HandledBySub);
    }

    private static HttpRequestMessage Req(HttpMethod method, string url, string json)
    {
        return new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
