using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

public sealed class ForumTagAdminIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_tagadmin_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
            b.UseSetting("Forum:Boards:1:Id", "tech");
            b.UseSetting("Forum:Boards:1:Title", "技术");
            b.UseSetting("Forum:Moderation:Moderators:0:Sub", "user-mod");
            b.UseSetting("Forum:Moderation:Moderators:0:BoardIds:0", "general");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
            });
        });
        Client = Factory.CreateClient();
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = mongo.GetDatabase(DatabaseName);
        var tags = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);

        var now = DateTime.UtcNow;

        await tags.InsertManyAsync(new[]
        {
            new ForumTagRecord
            {
                Id = "tag-active-1",
                Name = "科技",
                Slug = "科技",
                Status = ForumTagStatuses.Active,
                UseCount = 2,
                CreatedAtUtc = now.AddDays(-10),
                CreatedBySub = "seed",
            },
            new ForumTagRecord
            {
                Id = "tag-active-2",
                Name = "编程",
                Slug = "编程",
                Status = ForumTagStatuses.Active,
                UseCount = 1,
                CreatedAtUtc = now.AddDays(-9),
                CreatedBySub = "seed",
            },
            new ForumTagRecord
            {
                Id = "tag-disabled-1",
                Name = "已禁用",
                Slug = "已禁用",
                Status = ForumTagStatuses.Disabled,
                UseCount = 0,
                CreatedAtUtc = now.AddDays(-8),
                CreatedBySub = "seed",
            },
        });

        await posts.InsertManyAsync(new[]
        {
            new ForumPostRecord
            {
                Id = "tagtest-post-1",
                Title = "科技帖子1",
                Body = "body1",
                Excerpt = "exc1",
                AuthorSubId = "user-a",
                Board = "综合",
                Tags = ["科技", "编程"],
                CreatedAtUtc = now.AddDays(-5),
            },
            new ForumPostRecord
            {
                Id = "tagtest-post-2",
                Title = "科技帖子2",
                Body = "body2",
                Excerpt = "exc2",
                AuthorSubId = "user-b",
                Board = "综合",
                Tags = ["科技"],
                CreatedAtUtc = now.AddDays(-4),
            },
        });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }
}

[CollectionDefinition("ForumTagAdmin")]
public class ForumTagAdminCollection : ICollectionFixture<ForumTagAdminIntegrationFixture>
{
}

[Collection("ForumTagAdmin")]
public class ForumTagAdminApiTests
{
    private readonly ForumTagAdminIntegrationFixture _fx;

    public ForumTagAdminApiTests(ForumTagAdminIntegrationFixture fx) => _fx = fx;

    private static HttpRequestMessage AdminReq(HttpMethod method, string url, string? jsonBody = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("admin-user", ForumRoleClaim.Admin));
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return req;
    }

    private static HttpRequestMessage MemberReq(HttpMethod method, string url, string? jsonBody = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("member-user", ForumRoleClaim.Member));
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return req;
    }

    private static HttpRequestMessage ModeratorReq(HttpMethod method, string url, string? jsonBody = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokens.CreateAccessToken("mod-user", ForumRoleClaim.Moderator));
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return req;
    }

    private static async Task<string?> GetCode(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("code", out var codeEl))
            return codeEl.GetString();
        return null;
    }

    // 1. 普通成员调用 GET → 403
    [Fact]
    public async Task Get_tags_as_member_returns_403()
    {
        var req = MemberReq(HttpMethod.Get, "/api/forum/admin/tags");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // 1b. 版主调用 GET → 200（版主与 admin 均可访问）
    [Fact]
    public async Task Get_tags_as_moderator_returns_200()
    {
        var req = ModeratorReq(HttpMethod.Get, "/api/forum/admin/tags");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // 2. admin GET 返回所有标签
    [Fact]
    public async Task Admin_get_tags_returns_all()
    {
        var req = AdminReq(HttpMethod.Get, "/api/forum/admin/tags");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(items.GetArrayLength() >= 4);
    }

    // 3. 按 status=active 过滤
    [Fact]
    public async Task Admin_get_tags_filter_by_status_active()
    {
        var req = AdminReq(HttpMethod.Get, "/api/forum/admin/tags?status=active");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        foreach (var item in items.EnumerateArray())
            Assert.Equal("active", item.GetProperty("status").GetString());
    }

    // 4. admin POST 创建有效标签
    [Fact]
    public async Task Admin_create_tag_success()
    {
        var req = AdminReq(HttpMethod.Post, "/api/forum/admin/tags",
            "{\"name\": \"新标签测试\", \"description\": \"测试描述\"}");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("新标签测试", data.GetProperty("name").GetString());
        Assert.Equal("active", data.GetProperty("status").GetString());
        Assert.Equal(0, data.GetProperty("useCount").GetInt32());
    }

    // 5. admin POST 重复 slug → 409 TAG_SLUG_CONFLICT
    [Fact]
    public async Task Admin_create_duplicate_tag_returns_409()
    {
        // 科技已存在于 seed 数据
        var req = AdminReq(HttpMethod.Post, "/api/forum/admin/tags",
            "{\"name\": \"科技\"}");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("TAG_SLUG_CONFLICT", await GetCode(res));
    }

    // 6. admin PATCH 更新 name
    [Fact]
    public async Task Admin_patch_tag_name_success()
    {
        var req = AdminReq(HttpMethod.Patch, "/api/forum/admin/tags/tag-active-2",
            "{\"name\": \"编程更新\"}");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("编程更新", doc.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    // 7. admin POST {id}/disable（active tag）→ 成功变 disabled
    [Fact]
    public async Task Admin_disable_active_tag_success()
    {
        // 先创建一个新的 active tag 以免影响其他测试
        var createReq = AdminReq(HttpMethod.Post, "/api/forum/admin/tags",
            "{\"name\": \"待禁用标签\"}");
        var createRes = await _fx.Client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var createBody = await createRes.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        var tagId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var req = AdminReq(HttpMethod.Post, $"/api/forum/admin/tags/{tagId}/disable");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("disabled", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    // 8. admin POST {id}/enable（disabled tag）→ 成功变 active
    [Fact]
    public async Task Admin_enable_disabled_tag_success()
    {
        var req = AdminReq(HttpMethod.Post, "/api/forum/admin/tags/tag-disabled-1/enable");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("active", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    // 9. admin DELETE tag → 200（无论 active/disabled、有无 useCount）
    [Fact]
    public async Task Admin_delete_tag_success()
    {
        var createReq = AdminReq(HttpMethod.Post, "/api/forum/admin/tags",
            "{\"name\": \"待删除标签\"}");
        var createRes = await _fx.Client.SendAsync(createReq);
        var createBody = await createRes.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        var tagId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var req = AdminReq(HttpMethod.Delete, $"/api/forum/admin/tags/{tagId}");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // 10. admin DELETE tag with useCount > 0 → 200（帖子仍保留 tag 字符串，变为游离 tag）
    [Fact]
    public async Task Admin_delete_inuse_tag_success()
    {
        // 创建一个新 tag，直接在 MongoDB 把 UseCount 改为 3，再通过 API 删除
        var createReq = AdminReq(HttpMethod.Post, "/api/forum/admin/tags",
            "{\"name\": \"在用待删除标签\"}");
        var createRes = await _fx.Client.SendAsync(createReq);
        var createBody = await createRes.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        var tagId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var db = _fx.Factory.Services.GetRequiredService<IMongoClient>()
            .GetDatabase(_fx.DatabaseName);
        var col = db.GetCollection<ForumTagRecord>(ForumMongoSetup.TagsCollectionName);
        await col.UpdateOneAsync(x => x.Id == tagId,
            Builders<ForumTagRecord>.Update.Set(x => x.UseCount, 3));

        var req = AdminReq(HttpMethod.Delete, $"/api/forum/admin/tags/{tagId}");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // 额外：name 为空 → 400 INVALID_TAG_NAME
    [Fact]
    public async Task Admin_create_tag_empty_name_returns_400()
    {
        var req = AdminReq(HttpMethod.Post, "/api/forum/admin/tags",
            "{\"name\": \"   \"}");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("INVALID_TAG_NAME", await GetCode(res));
    }

    // 额外：name 超过 32 字符 → 400 INVALID_TAG_NAME
    [Fact]
    public async Task Admin_create_tag_name_too_long_returns_400()
    {
        var longName = new string('a', 33);
        var req = AdminReq(HttpMethod.Post, "/api/forum/admin/tags",
            $"{{\"name\": \"{longName}\"}}");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("INVALID_TAG_NAME", await GetCode(res));
    }

    // 额外：seed-from-posts 在非 Production 环境下可调用
    [Fact]
    public async Task Admin_seed_from_posts_in_dev_returns_200()
    {
        var req = AdminReq(HttpMethod.Post, "/api/forum/admin/tags/seed-from-posts");
        var res = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
