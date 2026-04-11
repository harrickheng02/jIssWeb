namespace JIssWeb.User.Api;

public class PasswordResetSettings
{
    public string Secret { get; set; } = "change-me-password-reset-secret";
    public string LinkBaseUrl { get; set; } = "http://localhost:5097/api/auth/reset-password";
    public string SuccessRedirectUrl { get; set; } = "http://localhost:5173/auth/reset";
    public int TokenTtlMinutes { get; set; } = 30;
    public int ForgotPerMinuteLimit { get; set; } = 5;
    public int ForgotCooldownSeconds { get; set; } = 60;
}
