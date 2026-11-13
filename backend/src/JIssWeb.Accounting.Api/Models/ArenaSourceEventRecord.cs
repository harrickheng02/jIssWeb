namespace JIssWeb.Accounting.Api.Models;

/// <summary>
/// 本地镜像 #33 不可变积分事件源（arena_points_events）。仅用于消费读取，字段与
/// JIssWeb.Model.Api.Models.ArenaPointsEventRecord 保持一致（#33 spec 已冻结该事件契约）。
/// 该集合物理上位于与 Model.Api 共享的 Mongo 库内。
/// </summary>
public class ArenaSourceEventRecord
{
    public string Id { get; set; } = "";
    public string EventId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "";
    public int Delta { get; set; }
    public string TargetUserId { get; set; } = "";
    public string AccusationId { get; set; } = "";
    public string WeekId { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public bool NotifyEligible { get; set; }
}

/// <summary>
/// #33 事件类型常量（与 Model.Api.ArenaPointsEventType 保持一致）。
/// </summary>
public static class ArenaSourceEventType
{
    public const string Hit = "accusation_hit";
    public const string Miss = "accusation_miss";
}
