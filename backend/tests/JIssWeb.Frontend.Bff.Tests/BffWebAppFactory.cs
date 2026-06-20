using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StackExchange.Redis;
using WireMock.Server;

namespace JIssWeb.Frontend.Bff.Tests;

public class BffWebAppFactory : WebApplicationFactory<Program>
{
    public WireMockServer UserApiMock { get; } = WireMockServer.Start();
    public WireMockServer CustomerApiMock { get; } = WireMockServer.Start();
    public WireMockServer ModelApiMock { get; } = WireMockServer.Start();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DownstreamServices:UserApiBaseUrl"] = UserApiMock.Url,
                ["DownstreamServices:CustomerApiBaseUrl"] = CustomerApiMock.Url,
                ["DownstreamServices:ModelApiBaseUrl"] = ModelApiMock.Url,
                ["Redis:ConnectionString"] = "localhost:6380",
                ["Jwt:Key"] = "test-key-must-be-at-least-32-chars!!!",
                ["Jwt:Issuer"] = "JIssWeb",
                ["Jwt:Audience"] = "JIssWeb",
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (descriptor != null) services.Remove(descriptor);

            var mockDb = new Mock<IDatabase>();
            // Lock acquisition always succeeds in tests — cover all StringSetAsync overloads
            mockDb.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<When>()))
                .ReturnsAsync(true);
            mockDb.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            mockDb.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            mockDb.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var mockMultiplexer = new Mock<IConnectionMultiplexer>();
            mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                .Returns(mockDb.Object);

            services.AddSingleton<IConnectionMultiplexer>(mockMultiplexer.Object);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            UserApiMock.Dispose();
            CustomerApiMock.Dispose();
            ModelApiMock.Dispose();
        }
    }
}
