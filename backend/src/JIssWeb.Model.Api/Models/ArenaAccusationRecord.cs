using MongoDB.Bson;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 指认业务记录（待结算）。status=pending，不设 7 天 TTL（D3 必修缺陷）；
/// 结算后由归档任务在 settledAtUtc 存在且距今 ≥14 天（实现取 21 天）时清理。
/// </summary>
public class ArenaAccusationRecord
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string WeekKey { get; set; } = "";
    public string AccuserId { get; set; } = "";
    public string TargetUserId { get; set; } = "";
    public string Status { get; set; } = ArenaAccusationStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SettledAtUtc { get; set; }
}
