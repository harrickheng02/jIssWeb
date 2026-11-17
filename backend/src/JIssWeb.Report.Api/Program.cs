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
var userDbName = builder.Configuration["Mongo:UserDatabaseName"] ?? "jissweb_user";
if (!string.IsNullOrEmpty(mongoConn))
{
    var mongoClient = new MongoClient(mongoConn);
    builder.Services.AddKeyedSingleton<IMongoDatabase>("arena-dashboard-model", mongoClient.GetDatabase(modelDbName));
    // 注册流入面板：只读投影用户库，不引 User.Api 程序集、不发服务间 HTTP。
    builder.Services.AddKeyedSingleton<IMongoDatabase>("arena-dashboard-user", mongoClient.GetDatabase(userDbName));
}
builder.Services.Configure<ArenaDashboardRevealOptions>(builder.Configuration.GetSection("ArenaReveal"));
builder.Services.Configure<RegistrationDashboardOptions>(
    builder.Configuration.GetSection(RegistrationDashboardOptions.SectionName));
builder.Services.AddScoped<IsAgentSubResolver>();
builder.Services.AddScoped<IArenaDashboardService, ArenaDashboardService>();

// 图灵名人堂（#37）：只读聚合出口，沿用同一 keyed 模型库。匿名端点（无 RequireForumModerator）。
builder.Services.AddScoped<IHunterNameResolver, MaskedHunterNameResolver>();
builder.Services.AddScoped<IHallOfTuringService, ArenaHallOfTuringService>();

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
