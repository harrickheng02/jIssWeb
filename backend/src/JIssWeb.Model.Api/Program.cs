using JIssWeb.Application;
using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;
using JIssWeb.Infrastructure;
using JIssWeb.Model.Api.Middleware;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.UseJIssWebHttpPort(5099);

builder.Services.Configure<ForumBoardsOptions>(builder.Configuration.GetSection(ForumBoardsOptions.SectionName));
builder.Services.Configure<ForumSearchRateLimitOptions>(builder.Configuration.GetSection(ForumSearchRateLimitOptions.SectionName));
builder.Services.AddSingleton<ForumSearchIpRateLimiter>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJIssWebCoreApi(builder.Configuration);

var app = builder.Build();

ForumMongoSetup.EnsureIndexes(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandling();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ForumSearchRateLimitMiddleware>();
app.MapControllers();
app.Run();

public partial class Program { }
