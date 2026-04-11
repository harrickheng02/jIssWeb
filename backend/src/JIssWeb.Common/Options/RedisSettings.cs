namespace JIssWeb.Common.Options;

public class RedisSettings
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6380";

    public string Password { get; set; } = "";

    public string KeyPrefix { get; set; } = "";
}
