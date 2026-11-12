using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo2Go;
using Moq;
using StackExchange.Redis;

namespace JIssWeb.Model.Api.Tests;

public class ForumSearchRateLimitTests : IAsyncLifetime
{
    private MongoDbRunner? _mongoRunner;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _mongoRunner = MongoDbRunner.Start();
        var dbName = "forum_rl_" + Guid.NewGuid().ToString("N");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mongo:ConnectionString", _mongoRunner.ConnectionString);
            b.UseSetting("Mongo:DatabaseName", dbName);
            b.UseSetting("Forum:SearchRateLimit:MaxRequests", "2");
            b.UseSetting("Forum:SearchRateLimit:WindowSeconds", "60");
            b.UseSetting("Forum:RateLimit:UseRedis", "false");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IConnectionMultiplexer));
                services.AddSingleton<IConnectionMultiplexer>(_ => Mock.Of<IConnectionMultiplexer>());
            });
        });
        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory != null)
            await _factory.DisposeAsync();
        _mongoRunner?.Dispose();
    }

    [Fact]
    public async Task Search_ThirdRequestWithQ_Returns429()
    {
        Assert.NotNull(_client);
        for (var i = 0; i < 3; i++)
        {
            var res = await _client.GetAsync("/api/forum/posts?q=x");
            if (i < 2)
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            else
            {
                Assert.Equal((HttpStatusCode)429, res.StatusCode);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                Assert.Equal("RATE_LIMITED", doc.RootElement.GetProperty("code").GetString());
            }
        }
    }
}
