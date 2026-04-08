using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.UseJIssWebHttpPort(5095);

builder.Services.AddJIssWebCoreApi(builder.Configuration);
builder.Services.Configure<DownstreamServicesOptions>(builder.Configuration.GetSection("DownstreamServices"));
builder.Services.AddHttpClient();

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

public class DownstreamServicesOptions
{
    public string UserApiBaseUrl { get; set; } = "http://localhost:5097";
    public string CustomerApiBaseUrl { get; set; } = "http://localhost:5098";
}
