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

/// <summary>
/// Integration tests for POST /api/mod/posts/{postId}/featured (加精 / 取消精华).
/// Re-uses <see cref="ForumMeIntegrationFixture"/> which already seeds me-post-a (综合) and me-post-tech (技术).
/// </summary>
public class ForumFeaturedOperationsTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;
    public ForumFeaturedOperationsTests(ForumMeIntegrationFixture fx) => _fx = fx;

    // ── auth guards ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Set_featured_without_bearer_returns_401()
    {
        var r = await _fx.Client.PostAsync("/api/mod/posts/me-post-a/featured",
            new StringContent("{\"isFeatured\":true}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Member_token_set_featured_returns_403()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/featured")
        {
            Content = new StringContent("{\"isFeatured\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Member)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ── 404 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Set_featured_unknown_post_returns_404()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/no-such-post-featured-xyz/featured")
        {
            Content = new StringContent("{\"isFeatured\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ── moderator board scope ─────────────────────────────────────────────────

    [Fact]
    public async Task Moderator_cannot_feature_post_outside_board_scope()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-tech/featured")
        {
            Content = new StringContent("{\"isFeatured\":true}", Encoding.UTF8, "application/json"),
            // user-mod is scoped to "general", not "tech"
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_can_feature_post_inside_board_scope()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/featured")
        {
            Content = new StringContent("{\"isFeatured\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // Cleanup: unset featured so other tests are not affected
        var unset = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/featured")
        {
            Content = new StringContent("{\"isFeatured\":false}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        await _fx.Client.SendAsync(unset);
    }

    // ── admin: set + list + detail reflect isFeatured ────────────────────────

    [Fact]
    public async Task Admin_can_set_featured_and_list_and_detail_reflect_it()
    {
        const string pid = "me-post-b";

        // Set featured
        var set = new HttpRequestMessage(HttpMethod.Post, $"/api/mod/posts/{pid}/featured")
        {
            Content = new StringContent("{\"isFeatured\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        var rSet = await _fx.Client.SendAsync(set);
        Assert.Equal(HttpStatusCode.OK, rSet.StatusCode);
        var setBody = await rSet.Content.ReadAsStringAsync();
        using var setDoc = JsonDocument.Parse(setBody);
        Assert.True(setDoc.RootElement.GetProperty("data").GetProperty("isFeatured").GetBoolean());

        // List should reflect isFeatured on that post
        var rList = await _fx.Client.GetAsync($"/api/forum/posts?featured=true");
        Assert.Equal(HttpStatusCode.OK, rList.StatusCode);
        var listBody = await rList.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listBody);
        var items = listDoc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        var found = false;
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("id").GetString() == pid)
            {
                Assert.True(item.GetProperty("isFeatured").GetBoolean());
                found = true;
            }
        }
        Assert.True(found, $"Post {pid} should appear in featured feed");

        // Detail should reflect isFeatured
        var rDetail = await _fx.Client.GetAsync($"/api/forum/posts/{pid}");
        Assert.Equal(HttpStatusCode.OK, rDetail.StatusCode);
        var detailBody = await rDetail.Content.ReadAsStringAsync();
        using var detailDoc = JsonDocument.Parse(detailBody);
        Assert.True(detailDoc.RootElement.GetProperty("data").GetProperty("isFeatured").GetBoolean());

        // Cleanup
        var unset = new HttpRequestMessage(HttpMethod.Post, $"/api/mod/posts/{pid}/featured")
        {
            Content = new StringContent("{\"isFeatured\":false}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        await _fx.Client.SendAsync(unset);
    }

    // ── idempotency: setting same value returns 200 without audit write ───────

    [Fact]
    public async Task Setting_featured_to_same_value_returns_200_idempotent()
    {
        // me-post-a starts as not-featured
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/featured")
        {
            Content = new StringContent("{\"isFeatured\":false}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("data").GetProperty("isFeatured").GetBoolean());
    }

    // ── audit log ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_set_featured_writes_audit_with_correct_actionLabel()
    {
        // Insert a temporary post to avoid polluting shared seed data
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        const string pid = "feat-audit-test-post";
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "audit-feat",
            Body = "b",
            Excerpt = "b",
            AuthorSubId = "user-a",
            Board = "综合",
            Tags = new List<string>(),
            CreatedAtUtc = DateTime.UtcNow,
        });
        try
        {
            // Add featured
            var set = new HttpRequestMessage(HttpMethod.Post, $"/api/mod/posts/{pid}/featured")
            {
                Content = new StringContent("{\"isFeatured\":true}", Encoding.UTF8, "application/json"),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
            };
            var rSet = await _fx.Client.SendAsync(set);
            Assert.Equal(HttpStatusCode.OK, rSet.StatusCode);

            var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/mod/audit?targetType=post&targetId={pid}&page=1&pageSize=20")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
            };
            var rAudit = await _fx.Client.SendAsync(auditReq);
            Assert.Equal(HttpStatusCode.OK, rAudit.StatusCode);
            var auditBody = await rAudit.Content.ReadAsStringAsync();
            using var auditDoc = JsonDocument.Parse(auditBody);
            var auditItems = auditDoc.RootElement.GetProperty("data").GetProperty("items");
            Assert.True(auditItems.GetArrayLength() >= 1);
            var label = auditItems[0].GetProperty("actionLabel").GetString();
            Assert.Equal("加精", label);

            // Remove featured
            var unset = new HttpRequestMessage(HttpMethod.Post, $"/api/mod/posts/{pid}/featured")
            {
                Content = new StringContent("{\"isFeatured\":false}", Encoding.UTF8, "application/json"),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
            };
            var rUnset = await _fx.Client.SendAsync(unset);
            Assert.Equal(HttpStatusCode.OK, rUnset.StatusCode);

            var auditReq2 = new HttpRequestMessage(HttpMethod.Get, $"/api/mod/audit?targetType=post&targetId={pid}&page=1&pageSize=20")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
            };
            var rAudit2 = await _fx.Client.SendAsync(auditReq2);
            Assert.Equal(HttpStatusCode.OK, rAudit2.StatusCode);
            var auditBody2 = await rAudit2.Content.ReadAsStringAsync();
            using var auditDoc2 = JsonDocument.Parse(auditBody2);
            var auditItems2 = auditDoc2.RootElement.GetProperty("data").GetProperty("items");
            Assert.True(auditItems2.GetArrayLength() >= 2);
            // Newest first
            var label2 = auditItems2[0].GetProperty("actionLabel").GetString();
            Assert.Equal("取消精华", label2);
        }
        finally
        {
            await audit.DeleteManyAsync(x => x.TargetId == pid);
            await posts.DeleteManyAsync(x => x.Id == pid);
        }
    }
}
