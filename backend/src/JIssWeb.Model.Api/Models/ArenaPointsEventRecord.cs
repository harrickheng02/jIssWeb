using MongoDB.Bson;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 不可变积分事件（方案 C 积分事件最小桩）。只追加、永不更新、零运行时状态字段。
/// eventId 由 (weekId, userId, targetUserId, type) 确定性派生，保证重放幂等。
/// notifyEligible 为创建时常量（hit=true / miss=false），非投递状态（D9）。
/// </summary>
public class ArenaPointsEventRecord
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string EventId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "";
    public int Delta { get; set; }
    public string TargetUserId { get; set; } = "";
    public string AccusationId { get; set; } = "";
    public string WeekId { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public bool NotifyEligible { get; set; }
}
