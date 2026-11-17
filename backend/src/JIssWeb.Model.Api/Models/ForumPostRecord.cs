namespace JIssWeb.Model.Api.Models;

public class ForumPostRecord
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string AuthorSubId { get; set; } = "";
    public string Board { get; set; } = "综合";
    public List<string> Tags { get; set; } = new();
    public bool IsSticky { get; set; }
    public DateTime? StickyAtUtc { get; set; }
    public string? StickyBySub { get; set; }
    /// <summary>When true, new replies are rejected (moderation lock).</summary>
    public bool RepliesLocked { get; set; }
    public DateTime? RepliesLockedAtUtc { get; set; }
    public string? RepliesLockedBySub { get; set; }
    /// <summary>When true, the post has been marked as a featured/精华 post by a moderator.</summary>
    public bool IsFeatured { get; set; }
    public DateTime? FeaturedAtUtc { get; set; }
    public string? FeaturedBySub { get; set; }
    public int LikeCount { get; set; }
    public int FavoriteCount { get; set; }
    public int CommentCount { get; set; }
    public int ViewCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string State { get; set; } = "published";  // published | draft | deleted | pending_review
    /// <summary>AI 版主阶段一：命中的判定类别（spam / sexual / political / other）。</summary>
    public string? ModerationFlag { get; set; }
    /// <summary>AI 版主阶段一：判定置信度（0–1）。</summary>
    public double? ModerationConfidence { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBySub { get; set; }
}
