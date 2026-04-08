using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.UseJIssWebHttpPort(5094);

builder.Services.AddJIssWebCoreApi(builder.Configuration);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

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
app.MapReverseProxy();
app.Run();
