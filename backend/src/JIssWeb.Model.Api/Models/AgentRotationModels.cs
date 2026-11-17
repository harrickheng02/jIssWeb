using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 账号轮换触发源标识（V0.3）。runKey 形如 <c>&lt;trigger&gt;:&lt;scoped-key&gt;</c>。
/// </summary>
public static class AgentRotationTriggers
{
    /// <summary>周结算完成后触发，scoped-key 为周键。</summary>
    public const string Reveal = "reveal";

    /// <summary>后台定时兜底，scoped-key 为 yyyy-MM-dd。</summary>
    public const string Daily = "daily";

    /// <summary>运营手动触发，scoped-key 为 yyyyMMddHHmmss。</summary>
    public const string Manual = "manual";
}

/// <summary>
/// 账号轮换运行记录（V0.3，按 runKey 唯一幂等认领）。
/// </summary>
[BsonIgnoreExtraElements]
public sealed class AgentRotationRunRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>幂等键：<c>&lt;trigger&gt;:&lt;scoped-key&gt;</c>，唯一索引。</summary>
    public string RunKey { get; set; } = "";

    /// <summary>触发源，见 <see cref="AgentRotationTriggers"/>。</summary>
    public string Trigger { get; set; } = "";

    /// <summary>运行前的缺口（<c>MinActiveAgents - activeCount</c>，可为负）。</summary>
    public int GapBefore { get; set; }

    /// <summary>本次成功创建并落库的替代者数量。</summary>
    public int CreatedCount { get; set; }

    /// <summary>本次失败数量（建号失败、persona 写入失败等）。</summary>
    public int FailedCount { get; set; }

    /// <summary>运行结束时的 <c>state=active</c> 计数。</summary>
    public int ActiveCountAfter { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>手动触发时的操作者 sub；事件/定时通道为空。</summary>
    public string? TriggeredBySub { get; set; }

    /// <summary>失败摘要（最多保留前若干条），便于排障；不对外暴露敏感信息。</summary>
    public List<string> Failures { get; set; } = new();
}
