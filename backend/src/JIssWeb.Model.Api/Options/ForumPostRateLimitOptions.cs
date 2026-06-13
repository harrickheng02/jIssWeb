namespace JIssWeb.Model.Api.Options;

public sealed class ForumPostRateLimitOptions
{
    public const string SectionName = "Forum:PostRateLimit";

    public int MaxPosts { get; set; } = 10;
    public int MaxReplies { get; set; } = 30;
    public int WindowSeconds { get; set; } = 60;
}
