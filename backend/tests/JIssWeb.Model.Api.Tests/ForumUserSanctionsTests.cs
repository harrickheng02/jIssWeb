using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumUserSanctions")]
public sealed class ForumUserSanctionsTests : IClassFixture<ForumUserSanctionsIntegrationFixture>
{
    private readonly ForumUserSanctionsIntegrationFixture _fx;

    public ForumUserSanctionsTests(ForumUserSanctionsIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task Muted_user_cannot_create_post()
    {
        _fx.Sanctions.SetMuted("user-muted");
        var body = JsonSerializer.Serialize(new { title = "t", body = "b", boardId = "general" });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-muted")) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var json = await r.Content.ReadAsStringAsync();
        Assert.Contains("FORUM_MUTED", json);
    }

    [Fact]
    public async Task Non_muted_user_can_create_post()
    {
        _fx.Sanctions.ClearMute("user-ok");
        var body = JsonSerializer.Serialize(new { title = "ok", body = "b", boardId = "general" });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-ok")) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_can_issue_warning_without_blocking_author()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/users/user-author/sanctions")
        {
            Content = JsonContent.Create(new
            {
                type = "warning",
                reason = "首次违规提醒",
                reportId = "san-report-1",
            }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" })) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        _fx.Sanctions.ClearMute("user-author");
        var postBody = JsonSerializer.Serialize(new { title = "after warn", body = "b", boardId = "general" });
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts")
        {
            Content = new StringContent(postBody, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-author")) },
        };
        var postR = await _fx.Client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postR.StatusCode);
    }

    [Fact]
    public async Task Moderator_mute_blocks_author_writes()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/users/user-author/sanctions")
        {
            Content = JsonContent.Create(new
            {
                type = "mute",
                reason = "重复灌水",
                reportId = "san-report-1",
                durationPreset = "24h",
            }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" })) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var postBody = JsonSerializer.Serialize(new { title = "blocked", body = "b", boardId = "general" });
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts")
        {
            Content = new StringContent(postBody, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-author")) },
        };
        var postR = await _fx.Client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Forbidden, postR.StatusCode);
    }

    [Fact]
    public async Task Warning_writes_forum_warning_notification()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/users/user-author/sanctions")
        {
            Content = JsonContent.Create(new
            {
                type = "warning",
                reason = "违规内容",
                reportId = "san-report-1",
            }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" })) },
        };
        await _fx.Client.SendAsync(req);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var notes = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var note = await notes.Find(x => x.RecipientSubId == "user-author" && x.Type == InAppNotificationTypes.ForumWarning).FirstOrDefaultAsync();
        Assert.NotNull(note);
    }

    [Fact]
    public async Task Delete_from_report_queue_without_reason_succeeds()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/mod/posts/san-post-1")
        {
            Content = JsonContent.Create(new { reportId = "san-report-1", reason = "" }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" })) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Delete_from_report_queue_writes_audit_reportId()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/mod/posts/san-post-1")
        {
            Content = JsonContent.Create(new { reportId = "san-report-1" }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" })) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        var row = await audit.Find(x => x.Action == "post.modDelete").FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.True(row!.Metadata!.ContainsKey("reportId"));
        Assert.Equal("san-report-1", row.Metadata["reportId"]?.ToString());
    }

    [Fact]
    public async Task Sanction_without_reason_returns_400()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/users/user-author/sanctions")
        {
            Content = JsonContent.Create(new { type = "warning", reason = "", reportId = "san-report-1" }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" })) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_can_issue_warning_via_report_endpoint()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/reports/san-report-1/sanctions")
        {
            Content = JsonContent.Create(new
            {
                type = "warning",
                reason = "首次违规提醒",
            }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" })) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Reply_report_sanction_resolves_reply_author_not_post_author()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        var reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);

        await replies.InsertOneAsync(new ForumReplyRecord
        {
            Id = "san-reply-1",
            PostId = "san-post-1",
            AuthorSubId = "user-reply-author",
            Body = "违规回复",
            State = "deleted",
            CreatedAtUtc = DateTime.UtcNow,
        });

        await reports.InsertOneAsync(new ForumReportRecord
        {
            Id = "san-report-reply",
            ReporterSub = "user-reporter",
            TargetType = "reply",
            TargetId = "san-reply-1",
            PostId = "san-post-1",
            BoardId = "general",
            BoardTitle = "综合",
            Status = ForumReportStatuses.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/reports/san-report-reply/sanctions")
        {
            Content = JsonContent.Create(new
            {
                type = "warning",
                reason = "回复违规",
            }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator, new[] { "general" })) },
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var notes = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        var note = await notes.Find(x => x.RecipientSubId == "user-reply-author" && x.Type == InAppNotificationTypes.ForumWarning).FirstOrDefaultAsync();
        Assert.NotNull(note);
    }
}
