namespace JIssWeb.Model.Api.Options;

public sealed class ForumSearchRateLimitOptions
{
    public const string SectionName = "Forum:SearchRateLimit";

    public int MaxRequests { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
}
