namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 图灵场竞技域状态常量。统一用字符串，避免与既有 ForumContentStates 整形体系混淆。
/// </summary>
internal static class ArenaAccusationStatus
{
    internal const string Pending = "pending";
    internal const string Settled = "settled";
}

internal static class ArenaTallyStatus
{
    internal const string Active = "active";
    internal const string ThresholdReached = "threshold_reached";
    internal const string PendingReveal = "pending_reveal";
    internal const string VoidHuman = "void_human";
}

internal static class ArenaRevealJobState
{
    internal const string Settling = "settling";
    internal const string Settled = "settled";
    internal const string Revealing = "revealing";
    internal const string Completed = "completed";
}

internal static class ArenaPointsEventType
{
    internal const string Hit = "accusation_hit";
    internal const string Miss = "accusation_miss";
}
