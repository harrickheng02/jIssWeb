using MongoDB.Bson;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 周揭露批次。weekKey 唯一，状态 settling → settled → revealing → completed（D5 分阶段幂等）。
/// state=completed 配合 publishedAtUtc 是唯一的对外可见性门禁。
/// </summary>
public class ArenaRevealJobRecord
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string WeekKey { get; set; } = "";
    public string State { get; set; } = ArenaRevealJobState.Settling;
    public DateTime? PublishedAtUtc { get; set; }
    public int ProcessedCount { get; set; }
    /// <summary>本批次生成的「自白帖」Id 列表；completed 门禁后据此由 arena_pending 翻 published。</summary>
    public List<string> ConfessionPostIds { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
