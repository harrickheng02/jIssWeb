namespace JIssWeb.Model.Api.Services;

/// <summary>
/// AI 版主阶段一：内容可见性判定。
/// 公开列表/搜索/热榜依赖白名单式过滤器天然排除 <c>pending_review</c>；
/// 按 Id 直查的路径必须显式调用本 helper，避免待审内容从详情、回复列表或「我的内容」泄漏给无关用户。
/// </summary>
public static class ForumVisibility
{
    public const string PendingReviewState = "pending_review";

    public static bool IsPendingReview(string? state)
        => string.Equals(state, PendingReviewState, StringComparison.Ordinal);

    /// <summary>待审内容仅作者本人与版主/管理员可见；其他状态沿用既有可见性规则（返回 true）。</summary>
    public static bool CanViewPending(string? state, string? authorSubId, string? requesterSub, bool isModerator)
    {
        if (!IsPendingReview(state)) return true;
        if (isModerator) return true;
        return !string.IsNullOrEmpty(requesterSub)
               && string.Equals(authorSubId, requesterSub, StringComparison.Ordinal);
    }
}
