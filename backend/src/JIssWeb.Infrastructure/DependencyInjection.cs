using JIssWeb.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace JIssWeb.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoSettings>(configuration.GetSection(MongoSettings.SectionName));
        services.Configure<RedisSettings>(configuration.GetSection(RedisSettings.SectionName));

        services.AddSingleton<IMongoClient>(sp =>
        {
            var mongo = sp.GetRequiredService<IOptions<MongoSettings>>().Value;
            return new MongoClient(mongo.ConnectionString);
        });

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redis = sp.GetRequiredService<IOptions<RedisSettings>>().Value;
            var opts = ConfigurationOptions.Parse(redis.ConnectionString);
            if (!string.IsNullOrEmpty(redis.Password))
                opts.Password = redis.Password;
            return ConnectionMultiplexer.Connect(opts);
        });

        return services;
    }
}
