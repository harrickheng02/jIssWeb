using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace JIssWeb.User.Api.Services;

public interface IVerificationEmailSender
{
    Task SendVerificationEmailAsync(string toEmail, string verifyLink, DateTime expiresAtUtc);
}

public class ConsoleVerificationEmailSender : IVerificationEmailSender
{
    private readonly ILogger<ConsoleVerificationEmailSender> _logger;

    public ConsoleVerificationEmailSender(ILogger<ConsoleVerificationEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationEmailAsync(string toEmail, string verifyLink, DateTime expiresAtUtc)
    {
        _logger.LogInformation("verify-email to={Email} expires={ExpiresAtUtc} link={Link}", toEmail, expiresAtUtc, verifyLink);
        return Task.CompletedTask;
    }
}

public class SmtpVerificationEmailSender : IVerificationEmailSender
{
    private readonly SmtpEmailSettings _settings;

    public SmtpVerificationEmailSender(IOptions<SmtpEmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendVerificationEmailAsync(string toEmail, string verifyLink, DateTime expiresAtUtc)
    {
        ValidateSettings();

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromEmail, _settings.FromName),
            Subject = "JIssWeb 邮箱验证",
            Body = BuildHtmlBody(verifyLink, expiresAtUtc),
            IsBodyHtml = true
        };
        message.To.Add(toEmail);

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(_settings.Username))
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);

        await client.SendMailAsync(message);
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
            throw new InvalidOperationException("SMTP Host 未配置");
        if (string.IsNullOrWhiteSpace(_settings.FromEmail))
            throw new InvalidOperationException("SMTP 发件人邮箱未配置");
    }

    private static string BuildHtmlBody(string verifyLink, DateTime expiresAtUtc)
    {
        return $"""
                <html>
                  <body>
                    <p>欢迎注册 JIssWeb。</p>
                    <p>请点击下方链接完成邮箱验证：</p>
                    <p><a href="{WebUtility.HtmlEncode(verifyLink)}">{WebUtility.HtmlEncode(verifyLink)}</a></p>
                    <p>链接有效期至：{expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC</p>
                  </body>
                </html>
                """;
    }
}

public class SmtpEmailSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "JIssWeb";
    public bool EnableSsl { get; set; } = true;
}
