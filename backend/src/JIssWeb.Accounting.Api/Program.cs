using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Services;
using JIssWeb.Application;
using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;
using JIssWeb.Common.Options;
using JIssWeb.Infrastructure;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.UseJIssWebHttpPort(5100);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJIssWebCoreApi(builder.Configuration);

builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var mongo = sp.GetRequiredService<IOptions<MongoSettings>>().Value;
    return sp.GetRequiredService<IMongoClient>().GetDatabase(mongo.DatabaseName);
});
builder.Services.AddSingleton<ArenaSkillService>();
builder.Services.AddSingleton<ArenaPointsConsumerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ArenaPointsConsumerService>());
builder.Services.AddSingleton<ArenaPointsDecayService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ArenaPointsDecayService>());
builder.Services.AddSingleton<ArenaAiqiBatchService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ArenaAiqiBatchService>());

var app = builder.Build();

AccountingMongoSetup.EnsureIndexes(app.Services.GetRequiredService<IMongoDatabase>());

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
