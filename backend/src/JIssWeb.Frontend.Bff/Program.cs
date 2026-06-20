using System.Threading.RateLimiting;
using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;
using JIssWeb.Common.Options;
using JIssWeb.Frontend.Bff.Middleware;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
builder.UseJIssWebHttpPort(5095);

builder.Services.AddJIssWebCoreApi(builder.Configuration);
builder.Services.Configure<DownstreamServicesOptions>(builder.Configuration.GetSection("DownstreamServices"));

// Redis
var redisSettings = builder.Configuration.GetSection(RedisSettings.SectionName).Get<RedisSettings>() ?? new RedisSettings();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var opts = ConfigurationOptions.Parse(redisSettings.ConnectionString);
    if (!string.IsNullOrWhiteSpace(redisSettings.Password))
        opts.Password = redisSettings.Password;
    opts.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(opts);
});

// HttpClient
builder.Services.AddHttpClient();

// Rate limiter — bff-auth: 60s / 20 requests per IP
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("bff-auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(60),
                PermitLimit = 20,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsJsonAsync(
            JIssWeb.Common.ApiResult.Fail("请求过于频繁，请稍后重试", "RATE_LIMITED"));
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandling();
app.UseCors();
app.UseMiddleware<BffSourceHeaderMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }

public class DownstreamServicesOptions
{
    public string UserApiBaseUrl { get; set; } = "http://localhost:5097";
    public string CustomerApiBaseUrl { get; set; } = "http://localhost:5098";
    public string ModelApiBaseUrl { get; set; } = "http://localhost:5099";
    public int RefreshTokenCookieMaxAgeSeconds { get; set; } = 604800;
}
