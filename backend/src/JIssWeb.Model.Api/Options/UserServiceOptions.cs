namespace JIssWeb.Model.Api.Options;

public class InternalServiceOptions
{
    public const string SectionName = "InternalService";

    public string ApiKey { get; set; } = "";
}

public class UserServiceOptions
{
    public const string SectionName = "UserService";

    public string BaseUrl { get; set; } = "http://127.0.0.1:5097";
}
