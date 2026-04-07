using JIssWeb.Application;
using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;
using JIssWeb.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.UseJIssWebHttpPort(5101);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJIssWebCoreApi(builder.Configuration);

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
