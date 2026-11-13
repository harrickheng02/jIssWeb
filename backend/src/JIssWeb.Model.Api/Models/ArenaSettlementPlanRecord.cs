using MongoDB.Bson;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 单周揭露结算计划（prepare 阶段产出，不可变，零对外副作用）。
/// weekKey 唯一；批量判定 accountType 后落盘，commit 阶段据此写事件 / 自白帖 / 通知。
/// 查询失败则计划不落盘、整批不结算 / 不公开 / 不发通知，可从断点续跑（D5）。
/// </summary>
public class ArenaSettlementPlanRecord
{
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string WeekKey { get; set; } = "";
    public List<ArenaSettlementVerdict> Verdicts { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ArenaSettlementVerdict
{
    public string TargetUserId { get; set; } = "";
    /// <summary>是否为 AI 智能体（查 agent_personas.AgentUserId 命中）。</summary>
    public bool IsAgent { get; set; }
    /// <summary>hit（agent，将揭露并结算）/ miss（human，void_human 静默取消）。</summary>
    public string Verdict { get; set; } = "";
}
