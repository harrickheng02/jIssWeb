namespace JIssWeb.Common.Options;

public class RedisSettings
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6380";

    public string Password { get; set; } = "qq!219673605";

    public string KeyPrefix { get; set; } = "";
}
