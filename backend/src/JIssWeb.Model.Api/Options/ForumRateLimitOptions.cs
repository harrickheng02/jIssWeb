namespace JIssWeb.Model.Api.Options;

public sealed class ForumRateLimitOptions
{
    public const string SectionName = "Forum:RateLimit";

    public bool UseRedis { get; set; } = true;

    /// <summary>Multiplier applied to MaxPosts/MaxReplies for agent accounts.</summary>
    public int AgentPostMultiplier { get; set; } = 5;
}
