using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// AI 版主阶段一：复审队列端点。运营者最终拍板——approve 公开、reject 删除，
/// 两个动作都写审计；入队本身不写审计。
/// </summary>
[Collection("ContentModeration")]
public sealed class ModReviewQueueControllerTests
{
    private readonly ContentModerationIntegrationFixture _fx;

    public ModReviewQueueControllerTests(ContentModerationIntegrationFixture fx) => _fx = fx;

    private static HttpClient Moderator(HttpClient client, string sub = "mod-user")
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, "moderator"));
        return client;
    }

    private static HttpClient Member(HttpClient client, string sub = "plain-user")
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub));
        return client;
    }

    private IMongoCollection<ForumModerationAuditRecord> Audit()
        => _fx.Db().GetCollection<ForumModerationAuditRecord>("forum_moderation_audit");

    private async Task SeedPostAsync(string id, string state, string author = "queue-author")
        => await _fx.Posts().InsertOneAsync(new ForumPostRecord
        {
            Id = id,
            Title = "队列帖 " + id,
            Body = "正文 " + id,
            Excerpt = "正文",
            AuthorSubId = author,
            Board = "综合",
            Tags = new List<string>(),
            State = state,
            CreatedAtUtc = DateTime.UtcNow,
        });

    private async Task<string> SeedQueueAsync(string targetId, string status = "pending", string board = "general",
        string targetType = "post", string author = "queue-author")
    {
        var row = new ForumModerationQueueRecord
        {
            TargetType = targetType,
            TargetId = targetId,
            PostId = targetId,
            BoardId = board,
            AuthorSubId = author,
            Source = ForumModerationSources.Llm,
            Status = status,
            Verdict = ForumModerationVerdicts.Spam,
            Confidence = 0.95,
            Reason = "广告",
            CreatedAtUtc = DateTime.UtcNow,
        };
        await _fx.Queue().InsertOneAsync(row);
        return row.Id;
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        using var client = _fx.Factory.CreateClient();
        var res = await client.GetAsync("/api/mod/review-queue");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Member_is_forbidden()
    {
        using var client = Member(_fx.Factory.CreateClient());
        var res = await client.GetAsync("/api/mod/review-queue");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task List_filters_by_status_and_pages()
    {
        await SeedQueueAsync("queue-list-1", "pending");
        await SeedQueueAsync("queue-list-2", "flagged");
        using var client = Moderator(_fx.Factory.CreateClient());

        var res = await client.GetAsync("/api/mod/review-queue?status=pending&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        var ids = items.EnumerateArray().Select(x => x.GetProperty("targetId").GetString()).ToList();
        Assert.Contains("queue-list-1", ids);
        Assert.DoesNotContain("queue-list-2", ids);
    }

    [Fact]
    public async Task List_isolates_boards_for_scoped_moderator()
    {
        await SeedQueueAsync("queue-board-1", board: "general");
        await SeedQueueAsync("queue-board-2", board: "tech");
        using var client = Moderator(_fx.Factory.CreateClient());

        var res = await client.GetAsync("/api/mod/review-queue?page=1&pageSize=50");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var ids = doc.RootElement.GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(x => x.GetProperty("targetId").GetString()).ToList();

        Assert.Contains("queue-board-1", ids);
        Assert.DoesNotContain("queue-board-2", ids);
    }

    [Fact]
    public async Task Approve_publishes_content_and_writes_audit()
    {
        await SeedPostAsync("queue-approve-1", "pending_review");
        var queueId = await SeedQueueAsync("queue-approve-1");
        using var client = Moderator(_fx.Factory.CreateClient());

        var res = await client.PostAsync($"/api/mod/review-queue/{queueId}/approve", null);
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, body);

        var post = await _fx.Posts().Find(x => x.Id == "queue-approve-1").FirstOrDefaultAsync();
        Assert.Equal("published", post!.State);

        var row = await _fx.Queue().Find(x => x.TargetId == "queue-approve-1").FirstOrDefaultAsync();
        Assert.Equal(ForumModerationQueueStatuses.Approved, row!.Status);
        Assert.Equal("mod-user", row.HandledBySub);
        Assert.NotNull(row.HandledAtUtc);

        var audit = await Audit().Find(x => x.TargetId == "queue-approve-1").FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("content.approve", audit!.Action);
        Assert.Equal("mod-user", audit.OperatorSub);
    }

    [Fact]
    public async Task Reject_deletes_content_with_system_actor_and_audits_verdict()
    {
        await SeedPostAsync("queue-reject-1", "pending_review");
        var queueId = await SeedQueueAsync("queue-reject-1");
        using var client = Moderator(_fx.Factory.CreateClient());

        var res = await client.PostAsync($"/api/mod/review-queue/{queueId}/reject", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var post = await _fx.Posts().Find(x => x.Id == "queue-reject-1").FirstOrDefaultAsync();
        Assert.Equal("deleted", post!.State);
        Assert.Equal("system:ai-moderator", post.DeletedBySub);

        var audit = await Audit().Find(x => x.TargetId == "queue-reject-1").FirstOrDefaultAsync();
        Assert.Equal("content.reject", audit!.Action);
        Assert.NotNull(audit.Metadata);
        Assert.True(audit.Metadata!.ContainsKey("verdict"));
        Assert.True(audit.Metadata.ContainsKey("confidence"));
    }

    [Fact]
    public async Task Enqueue_alone_writes_no_audit()
    {
        var before = await Audit().CountDocumentsAsync(FilterDefinition<ForumModerationAuditRecord>.Empty);
        await SeedPostAsync("queue-silent-1", "pending_review");
        await SeedQueueAsync("queue-silent-1");

        var after = await Audit().CountDocumentsAsync(FilterDefinition<ForumModerationAuditRecord>.Empty);
        Assert.Equal(before, after);
    }
}
