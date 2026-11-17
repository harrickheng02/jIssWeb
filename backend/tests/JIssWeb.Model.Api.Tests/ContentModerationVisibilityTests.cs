using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// AI 版主阶段一：待审内容的可见性回归。
/// 公开面 404、作者与版主 200 且带标记、待审帖禁止回复、公开列表与「我的内容」行为正确。
/// </summary>
[Collection("ContentModeration")]
public sealed class ContentModerationVisibilityTests
{
    private readonly ContentModerationIntegrationFixture _fx;

    public ContentModerationVisibilityTests(ContentModerationIntegrationFixture fx) => _fx = fx;

    private static HttpClient WithToken(HttpClient client, string sub, string role = "member")
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken(sub, role));
        return client;
    }

    private async Task SeedPendingPostAsync(string id, string author = "pending-author")
    {
        await _fx.Posts().InsertOneAsync(new ForumPostRecord
        {
            Id = id,
            Title = "待审标题",
            Body = "待审正文",
            Excerpt = "待审正文",
            AuthorSubId = author,
            Board = "综合",
            Tags = new List<string>(),
            State = "pending_review",
            ModerationFlag = ForumModerationVerdicts.Spam,
            ModerationConfidence = 0.95,
            CreatedAtUtc = DateTime.UtcNow,
        });
    }

    [Fact]
    public async Task Anonymous_detail_of_pending_post_is_404()
    {
        await SeedPendingPostAsync("vis-post-1");
        using var client = _fx.Factory.CreateClient();

        var res = await client.GetAsync("/api/forum/posts/vis-post-1");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Author_sees_own_pending_post_with_marker()
    {
        await SeedPendingPostAsync("vis-post-2");
        using var client = WithToken(_fx.Factory.CreateClient(), "pending-author");

        var res = await client.GetAsync("/api/forum/posts/vis-post-2");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("pendingReview").GetBoolean());
        Assert.Equal("pending_review", data.GetProperty("state").GetString());
        Assert.Equal(ForumModerationVerdicts.Spam, data.GetProperty("moderationVerdict").GetString());
    }

    [Fact]
    public async Task Moderator_sees_pending_post()
    {
        await SeedPendingPostAsync("vis-post-3");
        using var client = WithToken(_fx.Factory.CreateClient(), "mod-user", "moderator");

        var res = await client.GetAsync("/api/forum/posts/vis-post-3");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Other_member_gets_404_for_pending_post()
    {
        await SeedPendingPostAsync("vis-post-4");
        using var client = WithToken(_fx.Factory.CreateClient(), "someone-else");

        var res = await client.GetAsync("/api/forum/posts/vis-post-4");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Reply_to_pending_post_is_rejected_without_writing()
    {
        await SeedPendingPostAsync("vis-post-5");
        using var client = WithToken(_fx.Factory.CreateClient(), "replier-1");

        var res = await client.PostAsync("/api/forum/posts/vis-post-5/replies",
            new StringContent(JsonSerializer.Serialize(new { body = "回复" }), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(0, await _fx.Replies().CountDocumentsAsync(x => x.PostId == "vis-post-5"));
    }

    [Fact]
    public async Task Pending_reply_is_visible_only_to_its_author()
    {
        await _fx.Posts().InsertOneAsync(new ForumPostRecord
        {
            Id = "vis-post-6",
            Title = "正常帖",
            Body = "正常正文",
            Excerpt = "正常正文",
            AuthorSubId = "post-author",
            Board = "综合",
            Tags = new List<string>(),
            State = "published",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await _fx.Replies().InsertOneAsync(new ForumReplyRecord
        {
            Id = "vis-reply-1",
            PostId = "vis-post-6",
            AuthorSubId = "reply-author",
            Body = "待审回复",
            State = "pending_review",
            ModerationFlag = ForumModerationVerdicts.Spam,
            CreatedAtUtc = DateTime.UtcNow,
        });

        using var anonymous = _fx.Factory.CreateClient();
        var anonRes = await anonymous.GetAsync("/api/forum/posts/vis-post-6/replies");
        using (var doc = JsonDocument.Parse(await anonRes.Content.ReadAsStringAsync()))
            Assert.Equal(0, doc.RootElement.GetProperty("data").GetArrayLength());

        using var author = WithToken(_fx.Factory.CreateClient(), "reply-author");
        var authorRes = await author.GetAsync("/api/forum/posts/vis-post-6/replies");
        using (var doc = JsonDocument.Parse(await authorRes.Content.ReadAsStringAsync()))
        {
            var items = doc.RootElement.GetProperty("data");
            Assert.Equal(1, items.GetArrayLength());
            Assert.True(items[0].GetProperty("pendingReview").GetBoolean());
        }
    }

    [Fact]
    public async Task Public_list_excludes_pending_posts()
    {
        await SeedPendingPostAsync("vis-post-7");
        using var client = _fx.Factory.CreateClient();

        foreach (var url in new[]
                 {
                     "/api/forum/posts?page=1&pageSize=50",
                     "/api/forum/posts?page=1&pageSize=50&sort=hot",
                     "/api/forum/posts?page=1&pageSize=50&q=待审标题",
                 })
        {
            var res = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var text = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain("vis-post-7", text);
            Assert.DoesNotContain("待审标题", text);
        }
    }

    [Fact]
    public async Task My_posts_includes_own_pending_item_with_marker()
    {
        await SeedPendingPostAsync("vis-post-8", "my-pending-author");
        using var client = WithToken(_fx.Factory.CreateClient(), "my-pending-author");

        var res = await client.GetAsync("/api/forum/me/posts?page=1&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        var found = items.EnumerateArray().FirstOrDefault(x => x.GetProperty("id").GetString() == "vis-post-8");
        Assert.NotEqual(JsonValueKind.Undefined, found.ValueKind);
        Assert.True(found.GetProperty("pendingReview").GetBoolean());
    }

    [Fact]
    public async Task Pending_post_can_still_be_reported()
    {
        await SeedPendingPostAsync("vis-post-9", "report-target-author");
        using var client = WithToken(_fx.Factory.CreateClient(), "reporter-1");

        var res = await client.PostAsync("/api/forum/reports",
            new StringContent(JsonSerializer.Serialize(new
            {
                targetType = "post",
                targetId = "vis-post-9",
                reason = "疑似违规",
            }), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
