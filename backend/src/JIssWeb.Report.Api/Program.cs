using JIssWeb.Application;
using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;
using JIssWeb.Infrastructure;
using JIssWeb.Report.Api.Models;
using JIssWeb.Report.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.UseJIssWebHttpPort(5101);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJIssWebCoreApi(builder.Configuration);

// 运营仪表盘：只读聚合层。连共享 Mongo 库（jissweb_model）聚合各集合，不得写入。
var mongoConn = builder.Configuration["Mongo:ConnectionString"];
var modelDbName = builder.Configuration["Mongo:ModelDatabaseName"] ?? "jissweb_model";
if (!string.IsNullOrEmpty(mongoConn))
{
    var mongoClient = new MongoClient(mongoConn);
    builder.Services.AddKeyedSingleton<IMongoDatabase>("arena-dashboard-model", mongoClient.GetDatabase(modelDbName));
}
builder.Services.Configure<ArenaDashboardRevealOptions>(builder.Configuration.GetSection("ArenaReveal"));
builder.Services.AddScoped<IsAgentSubResolver>();
builder.Services.AddScoped<IArenaDashboardService, ArenaDashboardService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandling();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
