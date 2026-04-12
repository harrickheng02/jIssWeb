namespace JIssWeb.Common.Options;

public class MongoSettings
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "";

    public string DatabaseName { get; set; } = "jissweb";

    public string CustomerDatabaseName { get; set; } = "jissweb_customer";
}
