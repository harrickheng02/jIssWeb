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

public class ForumModerationStickyTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumModerationStickyTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task Set_sticky_without_bearer_returns_401()
    {
        var r = await _fx.Client.PostAsync("/api/mod/posts/me-post-a/sticky",
            new StringContent("{\"isSticky\":true}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Member_token_set_sticky_returns_403()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/sticky")
        {
            Content = new StringContent("{\"isSticky\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Member)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Admin_can_set_sticky_and_list_and_detail_reflect_it()
    {
        var set = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/sticky")
        {
            Content = new StringContent("{\"isSticky\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        var rSet = await _fx.Client.SendAsync(set);
        Assert.Equal(HttpStatusCode.OK, rSet.StatusCode);

        var rList = await _fx.Client.GetAsync("/api/forum/posts?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, rList.StatusCode);
        var bodyList = await rList.Content.ReadAsStringAsync();
        using var docList = JsonDocument.Parse(bodyList);
        var firstId = docList.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("id").GetString();
        Assert.Equal("me-post-a", firstId);
        var firstSticky = docList.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("isSticky").GetBoolean();
        Assert.True(firstSticky);

        var rDetail = await _fx.Client.GetAsync("/api/forum/posts/me-post-a");
        Assert.Equal(HttpStatusCode.OK, rDetail.StatusCode);
        var bodyDetail = await rDetail.Content.ReadAsStringAsync();
        using var docDetail = JsonDocument.Parse(bodyDetail);
        Assert.True(docDetail.RootElement.GetProperty("data").GetProperty("isSticky").GetBoolean());
    }

    [Fact]
    public async Task Admin_audit_query_returns_setSticky_record()
    {
        var set = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-b/sticky")
        {
            Content = new StringContent("{\"isSticky\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        var rSet = await _fx.Client.SendAsync(set);
        Assert.Equal(HttpStatusCode.OK, rSet.StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/mod/audit?targetType=post&targetId=me-post-b&page=1&pageSize=20")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        var actionLabel = items[0].GetProperty("actionLabel").GetString();
        Assert.Equal("置顶帖子", actionLabel);
        Assert.False(items[0].TryGetProperty("action", out _));
        Assert.False(items[0].TryGetProperty("operatorSub", out _));
    }

    [Fact]
    public async Task Moderator_is_scoped_to_configured_board()
    {
        var reqGeneral = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/sticky")
        {
            Content = new StringContent("{\"isSticky\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
        };
        var rGeneral = await _fx.Client.SendAsync(reqGeneral);
        Assert.Equal(HttpStatusCode.OK, rGeneral.StatusCode);

        var reqTech = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-tech/sticky")
        {
            Content = new StringContent("{\"isSticky\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
        };
        var rTech = await _fx.Client.SendAsync(reqTech);
        Assert.Equal(HttpStatusCode.Forbidden, rTech.StatusCode);
    }

    [Fact]
    public async Task Moderator_audit_query_for_out_of_scope_post_returns_403()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/mod/audit?targetType=post&targetId=me-post-tech&page=1&pageSize=20")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_audit_query_for_in_scope_post_returns_200()
    {
        var stickyReq = new HttpRequestMessage(HttpMethod.Post, "/api/mod/posts/me-post-a/sticky")
        {
            Content = new StringContent("{\"isSticky\":true}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
        };
        var rSticky = await _fx.Client.SendAsync(stickyReq);
        Assert.Equal(HttpStatusCode.OK, rSticky.StatusCode);

        var auditReq = new HttpRequestMessage(HttpMethod.Get, "/api/mod/audit?targetType=post&targetId=me-post-a&page=1&pageSize=20")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
        };
        var r = await _fx.Client.SendAsync(auditReq);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Moderator_audit_query_unknown_target_returns_404()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/mod/audit?targetType=post&targetId=no-such-post-or-audit-xyz&page=1&pageSize=20")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Admin_audit_query_unknown_target_returns_200_empty()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/mod/audit?targetType=post&targetId=no-such-post-admin-empty-xyz&page=1&pageSize=20")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Moderator_audit_query_succeeds_when_post_deleted_but_audit_retains_board()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        const string pid = "mod-audit-post-deleted-scope";
        var t = DateTime.UtcNow;
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "del-audit",
            Body = "b",
            Excerpt = "b",
            AuthorSubId = "user-a",
            Board = "综合",
            Tags = new List<string>(),
            CreatedAtUtc = t,
            LikeCount = 0,
            CommentCount = 0,
            ViewCount = 0,
        });
        try
        {
            var sticky = new HttpRequestMessage(HttpMethod.Post, $"/api/mod/posts/{pid}/sticky")
            {
                Content = new StringContent("{\"isSticky\":true}", Encoding.UTF8, "application/json"),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-admin", ForumRoleClaim.Admin)) }
            };
            var rSticky = await _fx.Client.SendAsync(sticky);
            Assert.Equal(HttpStatusCode.OK, rSticky.StatusCode);

            await posts.DeleteOneAsync(x => x.Id == pid);

            var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/mod/audit?targetType=post&targetId={pid}&page=1&pageSize=20")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
            };
            var r = await _fx.Client.SendAsync(auditReq);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            Assert.True(doc.RootElement.GetProperty("data").GetProperty("items").GetArrayLength() >= 1);
        }
        finally
        {
            await audit.DeleteManyAsync(x => x.TargetId == pid);
            await posts.DeleteManyAsync(x => x.Id == pid);
        }
    }

    [Fact]
    public async Task Moderator_audit_query_succeeds_when_post_deleted_legacy_audit_board_title_only()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(_fx.DatabaseName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var audit = db.GetCollection<ForumModerationAuditRecord>(ForumMongoSetup.ModerationAuditCollectionName);
        const string pid = "mod-audit-legacy-board-only";
        var t = DateTime.UtcNow;
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = pid,
            Title = "legacy",
            Body = "b",
            Excerpt = "b",
            AuthorSubId = "user-a",
            Board = "综合",
            Tags = new List<string>(),
            CreatedAtUtc = t,
            LikeCount = 0,
            CommentCount = 0,
            ViewCount = 0,
        });
        try
        {
            await audit.InsertOneAsync(new ForumModerationAuditRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TargetType = "post",
                TargetId = pid,
                Action = "post.setSticky",
                OperatorSub = "user-admin",
                OccurredAtUtc = t,
                Metadata = new Dictionary<string, object> { ["board"] = "综合" },
            });

            await posts.DeleteOneAsync(x => x.Id == pid);

            var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/mod/audit?targetType=post&targetId={pid}&page=1&pageSize=20")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-mod", ForumRoleClaim.Moderator)) }
            };
            var r = await _fx.Client.SendAsync(auditReq);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("data").GetProperty("items");
            Assert.True(items.GetArrayLength() >= 1);
            var first = items[0];
            Assert.False(first.TryGetProperty("action", out _));
            Assert.False(first.TryGetProperty("operatorSub", out _));
            Assert.Equal("置顶帖子", first.GetProperty("actionLabel").GetString());
        }
        finally
        {
            await audit.DeleteManyAsync(x => x.TargetId == pid);
            await posts.DeleteManyAsync(x => x.Id == pid);
        }
    }
}

