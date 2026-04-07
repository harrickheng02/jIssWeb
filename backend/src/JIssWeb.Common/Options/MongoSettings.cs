namespace JIssWeb.Common.Options;

public class MongoSettings
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://harrickheng:qq%21219673605@localhost:37017/?authSource=admin";

    public string DatabaseName { get; set; } = "jissweb";
}
