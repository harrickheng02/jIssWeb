namespace JIssWeb.User.Api.Options;

public sealed class CaptchaSettings
{
    public const string SectionName = "Captcha";

    public bool Enabled { get; set; } = false;
    public string SecretKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 5;
}
