using MongoDB.Bson;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 指认去重记录。唯一索引 {accuserId, targetUserId} + TTL 7 天，专责验收①「7 天内只能指认一次」。
/// 生命周期与业务记录（arena_accusations）解耦：去重到期即清，业务记录保留至结算后归档。
/// </summary>
public class ArenaAccusationDedupeRecord
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string AccuserId { get; set; } = "";
    public string TargetUserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
