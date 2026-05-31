using JIssWeb.Application;
using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;
using JIssWeb.Common.Options;
using JIssWeb.Infrastructure;
using JIssWeb.Model.Api.Middleware;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.UseJIssWebHttpPort(5099);

builder.Services.AddOptions<ForumBoardsOptions>()
    .Bind(builder.Configuration.GetSection(ForumBoardsOptions.SectionName))
    .PostConfigure(o => o.Boards ??= new())
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ForumBoardsOptions>, ForumBoardsDuplicateTitleValidator>();
builder.Services.Configure<ForumModerationOptions>(builder.Configuration.GetSection(ForumModerationOptions.SectionName));
builder.Services.PostConfigure<ForumModerationOptions>(o => o.Moderators ??= new());
builder.Services.Configure<ForumSearchRateLimitOptions>(builder.Configuration.GetSection(ForumSearchRateLimitOptions.SectionName));
builder.Services.Configure<ForumReportRetentionOptions>(builder.Configuration.GetSection(ForumReportRetentionOptions.SectionName));
builder.Services.Configure<InternalServiceOptions>(builder.Configuration.GetSection(InternalServiceOptions.SectionName));
builder.Services.Configure<UserServiceOptions>(builder.Configuration.GetSection(UserServiceOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IUserSanctionClient, UserSanctionClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<UserServiceOptions>>().Value;
    var baseUrl = (opts.BaseUrl ?? "").Trim().TrimEnd('/');
    if (baseUrl.Length > 0)
        client.BaseAddress = new Uri(baseUrl + "/");
});
builder.Services.AddHostedService<ForumReportRetentionPurgeHostedService>();
builder.Services.Configure<ForumSoftDeleteOptions>(builder.Configuration.GetSection(ForumSoftDeleteOptions.SectionName));
builder.Services.AddHostedService<DraftCleanupBackgroundService>();
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection(RedisSettings.SectionName));
builder.Services.AddSingleton<ForumSearchIpRateLimiter>();
builder.Services.AddScoped<ForumAuthorDisplayResolver>();
builder.Services.AddSingleton<ForumReportTargetResolver>();
builder.Services.AddSingleton<ForumModerationAccessService>();
builder.Services.AddSingleton<ForumEngagementLikeCountCache>(sp =>
{
    var redisOpts = sp.GetRequiredService<IOptions<RedisSettings>>();
    var logger = sp.GetRequiredService<ILogger<ForumEngagementLikeCountCache>>();
    IConnectionMultiplexer? mux = null;
    var conn = redisOpts.Value.ConnectionString?.Trim() ?? "";
    if (!string.IsNullOrWhiteSpace(conn))
    {
        try
        {
            var options = ConfigurationOptions.Parse(conn);
            if (!string.IsNullOrEmpty(redisOpts.Value.Password))
                options.Password = redisOpts.Value.Password;
            mux = ConnectionMultiplexer.Connect(options);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis connection failed; like count cache disabled");
        }
    }

    return new ForumEngagementLikeCountCache(mux, redisOpts, logger);
});
builder.Services.AddScoped<ForumEngagementService>();
builder.Services.AddScoped<ForumModerationDeleteService>();
builder.Services.AddApplication();
builder.Services.AddMongoInfrastructure(builder.Configuration);
builder.Services.AddJIssWebCoreApi(builder.Configuration);

var app = builder.Build();

ForumMongoSetup.EnsureIndexes(app.Services);
await ForumMongoSetup.EnsureStateFieldAsync(app.Services);
await ForumMongoSetup.SeedInitialTagsAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandling();
app.UseCors();
if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api/forum/__debug"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next();
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ForumSearchRateLimitMiddleware>();
app.MapControllers();
app.Run();

public partial class Program { }
