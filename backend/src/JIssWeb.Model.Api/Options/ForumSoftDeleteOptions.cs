namespace JIssWeb.Model.Api.Options;

/// <summary>Controls the background physical-deletion pass for soft-deleted forum posts and replies.</summary>
public sealed class ForumSoftDeleteOptions
{
    public const string SectionName = "Forum:SoftDelete";

    /// <summary>Number of days a soft-deleted post or reply is kept before it is physically removed.</summary>
    public int RetentionDays { get; set; } = 30;
}
