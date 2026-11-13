using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Model.Api.Models;

/// <summary>
/// 智能体经验条目（Issue #32 对齐 pm-plan）。由调度器记忆回写内部生产，作为 #38 经验继承系统的数据源。
/// 本期不对外暴露 CRUD。
/// </summary>
[BsonIgnoreExtraElements]
public sealed class AgentExperienceRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    /// <summary>业务主键，唯一索引（幂等写入）。</summary>
    public string ExperienceId { get; set; } = "";

    /// <summary>来源智能体（agentUserId）。</summary>
    public string SourcePersonaId { get; set; } = "";

    /// <summary>模式类型，如 relationship_building / topic_safety / near_miss_handling / style。</summary>
    public string PatternType { get; set; } = "";

    public string Description { get; set; } = "";

    public List<string> ExampleTexts { get; set; } = new();

    public double EffectivenessScore { get; set; } = 1.0;

    public double Weight { get; set; } = 1.0;

    /// <summary>已继承该经验的后续 personaId 列表（多键索引）。</summary>
    public List<string> InheritedBy { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }
}
