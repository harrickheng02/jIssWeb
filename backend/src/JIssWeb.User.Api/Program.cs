using JIssWeb.Application;
using JIssWeb.Common.Hosting;
using JIssWeb.Common.Middleware;
using JIssWeb.Infrastructure;
using JIssWeb.User.Api.Controllers;
using JIssWeb.User.Api.Mongo;
using JIssWeb.User.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.UseJIssWebHttpPort(5097);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJIssWebCoreApi(builder.Configuration);
builder.Services.Configure<EmailVerificationSettings>(builder.Configuration.GetSection("EmailVerification"));
builder.Services.Configure<SmtpEmailSettings>(builder.Configuration.GetSection("SmtpEmail"));
builder.Services.Configure<LoginSecuritySettings>(builder.Configuration.GetSection("LoginSecurity"));
builder.Services.AddSingleton<ConsoleVerificationEmailSender>();
builder.Services.AddSingleton<SmtpVerificationEmailSender>();
builder.Services.AddSingleton<IVerificationEmailSender>(sp =>
{
    var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmtpEmailSettings>>().Value;
    return settings.Enabled
        ? sp.GetRequiredService<SmtpVerificationEmailSender>()
        : sp.GetRequiredService<ConsoleVerificationEmailSender>();
});

var app = builder.Build();
UserMongoSetup.EnsureIndexes(app.Services);

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
