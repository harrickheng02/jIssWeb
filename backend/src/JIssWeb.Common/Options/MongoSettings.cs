namespace JIssWeb.Common.Options;

public class MongoSettings
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    public string DatabaseName { get; set; } = "jissweb";
}
