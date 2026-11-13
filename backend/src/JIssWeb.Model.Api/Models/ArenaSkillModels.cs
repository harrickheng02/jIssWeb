using MongoDB.Bson;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 技能库存记录（与 Accounting.Api 共享同一 Mongo 库中的 user_skill_inventory 集合）。
/// Model.Api 仅读取并核销，绝不直接写库存的「购买/扣费」逻辑（那属 Accounting.Api）。
/// 字段与 JIssWeb.Accounting.Api.Models.UserSkillInventoryRecord 对齐。
/// </summary>
public class SkillInventoryRecord
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string UserId { get; set; } = "";
    public string SkillId { get; set; } = "";
    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Consumed { get; set; }
}

/// <summary>
/// 公开质询任务（内部集合 arena_inquiry_tasks）。记录目标、对应质询帖与 24h 回应窗口，
/// 作为 agent 调度感知的数据层钩子：AgentSchedulerEngine 可扫描本集合对未回应目标触发 agent 回应。
/// TargetUserId 仅存于本内部集合，绝不进入任何 API 响应（隐私红线）。
/// </summary>
public class ArenaInquiryTaskRecord
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string TargetUserId { get; set; } = "";
    public string InquiryPostId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime RespondByUtc { get; set; }
    public bool Responded { get; set; }
}

/// <summary>
/// 词云项：词 + 频次。响应中不含任何 targetUserId / eventId。
/// </summary>
public sealed class WordCloudItem(string Word, int Count)
{
    public string Word { get; } = Word;
    public int Count { get; } = Count;
}

public sealed class PublicInquiryRequest
{
    public string? TargetUserId { get; set; }
    public string? Question { get; set; }
}

public sealed class PublicInquiryResponse(string PostId)
{
    public string PostId { get; } = PostId;
}
