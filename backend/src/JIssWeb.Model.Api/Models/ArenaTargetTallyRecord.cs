using MongoDB.Bson;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 目标周维度指认计数。状态机 active → threshold_reached → pending_reveal | void_human。
/// threshold_reached 仅表示计数达阈值，不等于已确认为 agent（D4）。
/// </summary>
public class ArenaTargetTallyRecord
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string WeekKey { get; set; } = "";
    public string TargetUserId { get; set; } = "";
    public int Count { get; set; }
    public string Status { get; set; } = ArenaTallyStatus.Active;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
