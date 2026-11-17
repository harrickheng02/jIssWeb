namespace JIssWeb.Model.Api.Models;

internal static class ForumContentStates
{
    internal const string Published = "published";
    internal const string Draft = "draft";
    internal const string Deleted = "deleted";
    internal const string Local = "local";
    // 自白帖在揭露批次 prepare 期使用的私有态（D7）：对作者本人/版主/审核队列均不可见，
    // 仅在 reveal_jobs.state=completed 后由门禁统一转 published。绝不复用 Draft。
    internal const string ArenaPending = "arena_pending";
    // AI 版主阶段一：高置信度违规内容转入此态。仅作者本人与版主可见，
    // 由运营者在复审队列中 approve（→ published）或 reject（→ deleted）。
    internal const string PendingReview = "pending_review";
}
