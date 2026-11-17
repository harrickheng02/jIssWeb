using JIssWeb.Model.Api.Services;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// AI 版主阶段一：可见性判定的纯函数矩阵（5 种状态 × 4 种身份）。
/// 只有 pending_review 收窄可见范围，其余状态必须保持既有行为。
/// </summary>
public class ForumVisibilityTests
{
    private const string Author = "author-1";
    private const string Other = "other-1";

    [Theory]
    [InlineData("published", Author, false, true)]
    [InlineData("published", Other, false, true)]
    [InlineData("published", null, false, true)]
    [InlineData("published", Other, true, true)]
    [InlineData("draft", Other, false, true)]
    [InlineData("deleted", Other, false, true)]
    [InlineData("arena_pending", Other, false, true)]
    [InlineData("local", Other, false, true)]
    [InlineData("pending_review", Author, false, true)]
    [InlineData("pending_review", Author, true, true)]
    [InlineData("pending_review", Other, true, true)]
    [InlineData("pending_review", Other, false, false)]
    [InlineData("pending_review", null, false, false)]
    [InlineData("pending_review", null, true, true)]
    public void Visibility_matrix(string state, string? requester, bool isModerator, bool expected)
        => Assert.Equal(expected, ForumVisibility.CanViewPending(state, Author, requester, isModerator));

    [Fact]
    public void Is_pending_review_is_case_sensitive_and_null_safe()
    {
        Assert.True(ForumVisibility.IsPendingReview("pending_review"));
        Assert.False(ForumVisibility.IsPendingReview("Pending_Review"));
        Assert.False(ForumVisibility.IsPendingReview(null));
        Assert.False(ForumVisibility.IsPendingReview(""));
    }
}
