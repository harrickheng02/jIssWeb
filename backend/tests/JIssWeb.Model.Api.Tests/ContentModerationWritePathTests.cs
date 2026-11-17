using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// AI 版主阶段一：发帖/回帖写入口接入门禁。
/// 高置信度违规落库为 pending_review；低置信度保持 published 并入队标记；
/// 关闭开关时行为与基线完全一致。
/// </summary>
[Collection("ContentModeration")]
public sealed class ContentModerationWritePathTests
{
    private readonly ContentModerationIntegrationFixture _fx;

    public ContentModerationWritePathTests(ContentModerationIntegrationFixture fx) => _fx = fx;

    private static HttpRequestMessage AuthPost(string url, string sub, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub));
        return req;
    }

    private static void EnableModeration(Microsoft.AspNetCore.Hosting.IWebHostBuilder b)
        => b.UseSetting("Forum:ContentModeration:Enabled", "true");

    [Fact]
    public async Task High_confidence_post_is_persisted_as_pending_review()
    {
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.96, "广告");
        using var factory = _fx.CreateFactory(EnableModeration);
        using var client = factory.CreateClient();

        var res = await client.SendAsync(AuthPost("/api/forum/posts", "mod-author-1",
            new { title = "限时优惠", body = "点击链接领取", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal("pending_review", doc.RootElement.GetProperty("data").GetProperty("state").GetString());

        var saved = await _fx.Posts().Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Equal("pending_review", saved!.State);
        Assert.Equal(ForumModerationVerdicts.Spam, saved.ModerationFlag);
        Assert.Equal(0.96, saved.ModerationConfidence!.Value, 3);

        var queued = await _fx.Queue().Find(x => x.TargetId == id).FirstOrDefaultAsync();
        Assert.NotNull(queued);
        Assert.Equal(ForumModerationQueueStatuses.Pending, queued!.Status);
        Assert.Equal("post", queued.TargetType);
    }

    [Fact]
    public async Task Disabled_moderation_keeps_baseline_behaviour()
    {
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.99, "广告");
        var callsBefore = _fx.Moderator.Calls;
        using var factory = _fx.CreateFactory();
        using var client = factory.CreateClient();

        var res = await client.SendAsync(AuthPost("/api/forum/posts", "mod-author-2",
            new { title = "普通标题", body = "普通正文", boardId = "general" }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal("published", doc.RootElement.GetProperty("data").GetProperty("state").GetString());

        var saved = await _fx.Posts().Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.Equal("published", saved!.State);
        Assert.Null(saved.ModerationFlag);
        Assert.Equal(callsBefore, _fx.Moderator.Calls);
        Assert.Equal(0, await _fx.Queue().CountDocumentsAsync(x => x.TargetId == id));
    }

    [Fact]
    public async Task High_confidence_reply_is_pending_and_does_not_touch_comment_count()
    {
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.95, "广告");
        using var factory = _fx.CreateFactory(EnableModeration);
        using var client = factory.CreateClient();
        var before = (await _fx.Posts().Find(x => x.Id == "mod-post-1").FirstOrDefaultAsync())!.CommentCount;

        var res = await client.SendAsync(AuthPost("/api/forum/posts/mod-post-1/replies", "mod-replier-1",
            new { body = "加我微信看更多" }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal("pending_review", doc.RootElement.GetProperty("data").GetProperty("state").GetString());

        var reply = await _fx.Replies().Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.Equal("pending_review", reply!.State);

        var after = (await _fx.Posts().Find(x => x.Id == "mod-post-1").FirstOrDefaultAsync())!.CommentCount;
        Assert.Equal(before, after);

        var queued = await _fx.Queue().Find(x => x.TargetId == id).FirstOrDefaultAsync();
        Assert.NotNull(queued);
        Assert.Equal("reply", queued!.TargetType);
        Assert.Equal("mod-post-1", queued.PostId);
    }

    [Fact]
    public async Task Medium_confidence_reply_stays_published_and_counts()
    {
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.7, "疑似推广");
        using var factory = _fx.CreateFactory(EnableModeration);
        using var client = factory.CreateClient();
        var before = (await _fx.Posts().Find(x => x.Id == "mod-post-1").FirstOrDefaultAsync())!.CommentCount;

        var res = await client.SendAsync(AuthPost("/api/forum/posts/mod-post-1/replies", "mod-replier-2",
            new { body = "这是一条有点可疑但正常的回复" }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal("published", doc.RootElement.GetProperty("data").GetProperty("state").GetString());

        var after = (await _fx.Posts().Find(x => x.Id == "mod-post-1").FirstOrDefaultAsync())!.CommentCount;
        Assert.Equal(before + 1, after);

        var queued = await _fx.Queue().Find(x => x.TargetId == id).FirstOrDefaultAsync();
        Assert.Equal(ForumModerationQueueStatuses.Flagged, queued!.Status);
    }

    [Fact]
    public async Task Agent_authored_content_also_goes_through_gate()
    {
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.95, "智能体广告");
        using var factory = _fx.CreateFactory(EnableModeration);
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { title = "智能体发帖", body = "推广内容", boardId = "general" }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateAccessToken("agent-author-1", accountType: "agent"));
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var saved = await _fx.Posts().Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.Equal("pending_review", saved!.State);
    }

    private async Task SeedPostAsync(string id, string state = "published", int commentCount = 0, string author = "edit-author")
        => await _fx.Posts().InsertOneAsync(new ForumPostRecord
        {
            Id = id,
            Title = "原标题",
            Body = "原正文",
            Excerpt = "原正文",
            AuthorSubId = author,
            Board = "综合",
            Tags = new List<string>(),
            State = state,
            CommentCount = commentCount,
            CreatedAtUtc = DateTime.UtcNow,
        });

    [Fact]
    public async Task Editing_post_into_violation_moves_it_to_pending_review()
    {
        await SeedPostAsync("edit-post-1");
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.97, "改成广告");
        using var factory = _fx.CreateFactory(EnableModeration);
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Put, "/api/forum/posts/edit-post-1")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { body = "加微信领取优惠券" }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("edit-author"));
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var saved = await _fx.Posts().Find(x => x.Id == "edit-post-1").FirstOrDefaultAsync();
        Assert.Equal("pending_review", saved!.State);
        Assert.Equal(ForumModerationVerdicts.Spam, saved.ModerationFlag);

        var queued = await _fx.Queue().Find(x => x.TargetId == "edit-post-1").FirstOrDefaultAsync();
        Assert.Equal(ForumModerationQueueStatuses.Pending, queued!.Status);
    }

    [Fact]
    public async Task Editing_pending_content_cannot_self_clear()
    {
        await SeedPostAsync("edit-post-2", state: "pending_review");
        _fx.Moderator.Verdict = ModerationVerdict.Ok;
        using var factory = _fx.CreateFactory(EnableModeration);
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Put, "/api/forum/posts/edit-post-2")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { body = "已改干净" }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("edit-author"));
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var saved = await _fx.Posts().Find(x => x.Id == "edit-post-2").FirstOrDefaultAsync();
        Assert.Equal("pending_review", saved!.State);
    }

    [Fact]
    public async Task Editing_reply_into_violation_downgrades_and_frees_comment_count()
    {
        await SeedPostAsync("edit-post-3", commentCount: 1);
        await _fx.Replies().InsertOneAsync(new ForumReplyRecord
        {
            Id = "edit-reply-1",
            PostId = "edit-post-3",
            AuthorSubId = "edit-author",
            Body = "原回复",
            State = "published",
            CreatedAtUtc = DateTime.UtcNow,
        });
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.95, "改成广告");
        using var factory = _fx.CreateFactory(EnableModeration);
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Put, "/api/forum/posts/edit-post-3/replies/edit-reply-1")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { body = "点我主页看福利" }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("edit-author"));
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var reply = await _fx.Replies().Find(x => x.Id == "edit-reply-1").FirstOrDefaultAsync();
        Assert.Equal("pending_review", reply!.State);

        var post = await _fx.Posts().Find(x => x.Id == "edit-post-3").FirstOrDefaultAsync();
        Assert.Equal(0, post!.CommentCount);
    }

    [Fact]
    public async Task Publishing_draft_into_violation_lands_pending_review()
    {
        await SeedPostAsync("draft-1", state: "draft");
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.96, "广告");
        using var factory = _fx.CreateFactory(EnableModeration);
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts/drafts/draft-1/publish");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("edit-author"));
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("pending_review", doc.RootElement.GetProperty("data").GetProperty("state").GetString());

        var saved = await _fx.Posts().Find(x => x.Id == "draft-1").FirstOrDefaultAsync();
        Assert.Equal("pending_review", saved!.State);
    }
}
