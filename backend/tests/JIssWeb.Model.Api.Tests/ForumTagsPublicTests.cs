using System.Net;
using System.Text.Json;
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

public sealed class ForumTagsPublicIntegrationFixture : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DatabaseName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        DatabaseName = "model_tagspublic_" + Guid.NewGuid().ToString("N");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", DatabaseName);
            b.UseSetting("Forum:Boards:0:Id", "general");
            b.UseSetting("Forum:Boards:0:Title", "综合");
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
        var now = DateTime.UtcNow;

        // SeedInitialTagsAsync 在 startup 已插入 18 条，先清空再插入测试数据
        await tags.DeleteManyAsync(FilterDefinition<ForumTagRecord>.Empty);

        await tags.InsertManyAsync(new[]
        {
            new ForumTagRecord { Id = "pub-active-1", Name = "热门话题", Slug = "热门话题", Status = ForumTagStatuses.Active, UseCount = 5, CreatedAtUtc = now, CreatedBySub = "seed" },
            new ForumTagRecord { Id = "pub-active-2", Name = "技术分享", Slug = "技术分享", Status = ForumTagStatuses.Active, UseCount = 3, CreatedAtUtc = now, CreatedBySub = "seed" },
            new ForumTagRecord { Id = "pub-active-3", Name = "新手入门", Slug = "新手入门", Status = ForumTagStatuses.Active, UseCount = 1, CreatedAtUtc = now, CreatedBySub = "seed" },
            new ForumTagRecord { Id = "pub-disabled-1", Name = "已禁用标签", Slug = "已禁用标签", Status = ForumTagStatuses.Disabled, UseCount = 2, CreatedAtUtc = now, CreatedBySub = "seed" },
        });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }
}

[CollectionDefinition("ForumTagsPublic")]
public class ForumTagsPublicCollection : ICollectionFixture<ForumTagsPublicIntegrationFixture>
{
}

[Collection("ForumTagsPublic")]
public class ForumTagsPublicTests
{
    private readonly ForumTagsPublicIntegrationFixture _fx;

    public ForumTagsPublicTests(ForumTagsPublicIntegrationFixture fx) => _fx = fx;

    // popular 仅返回 active 标签
    [Fact]
    public async Task Popular_ReturnsOnlyActiveTags()
    {
        var res = await _fx.Client.GetAsync("/api/forum/tags/popular");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var names = await ParseStringList(res);
        Assert.DoesNotContain("已禁用标签", names);
        Assert.Contains("热门话题", names);
        Assert.Contains("技术分享", names);
        Assert.Contains("新手入门", names);
    }

    // popular 按 UseCount 降序排列
    [Fact]
    public async Task Popular_SortedByUseCountDesc()
    {
        var res = await _fx.Client.GetAsync("/api/forum/tags/popular");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var names = await ParseStringList(res);
        var idx热门 = names.IndexOf("热门话题");
        var idx技术 = names.IndexOf("技术分享");
        var idx新手 = names.IndexOf("新手入门");
        Assert.True(idx热门 < idx技术, "UseCount=5 的标签应排在 UseCount=3 之前");
        Assert.True(idx技术 < idx新手, "UseCount=3 的标签应排在 UseCount=1 之前");
    }

    // suggest 空 q 返回所有 active 标签
    [Fact]
    public async Task Suggest_EmptyQ_ReturnsAllActiveTags()
    {
        var res = await _fx.Client.GetAsync("/api/forum/tags/suggest");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var names = await ParseStringList(res);
        Assert.Contains("热门话题", names);
        Assert.Contains("技术分享", names);
        Assert.Contains("新手入门", names);
    }

    // suggest 按 q 过滤
    [Fact]
    public async Task Suggest_WithQ_FiltersActiveTagsByNameOrSlug()
    {
        var res = await _fx.Client.GetAsync("/api/forum/tags/suggest?q=技术");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var names = await ParseStringList(res);
        Assert.Contains("技术分享", names);
        Assert.DoesNotContain("热门话题", names);
        Assert.DoesNotContain("新手入门", names);
    }

    // suggest 不返回 disabled 标签
    [Fact]
    public async Task Suggest_DisabledTag_NotReturned()
    {
        var res = await _fx.Client.GetAsync("/api/forum/tags/suggest?q=已禁用");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var names = await ParseStringList(res);
        Assert.DoesNotContain("已禁用标签", names);
    }

    // suggest limit 参数超界 → 400
    [Fact]
    public async Task Suggest_LimitOutOfRange_Returns400()
    {
        var res = await _fx.Client.GetAsync("/api/forum/tags/suggest?limit=99");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private static async Task<List<string>> ParseStringList(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
    }
}
