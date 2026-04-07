namespace JIssWeb.Common.Options;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "JIssWeb";

    public string Audience { get; set; } = "JIssWeb";

    public string Key { get; set; } = "";
}
