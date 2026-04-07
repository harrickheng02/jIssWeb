namespace JIssWeb.Common.Options;

public class RedisSettings
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";

    public string KeyPrefix { get; set; } = "";
}
