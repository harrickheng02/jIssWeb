using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Report.Api.Models;

/// <summary>users 集合的最小只读投影。字段刻意只取注册面板需要的三项。</summary>
[BsonIgnoreExtraElements]
public sealed class DashboardUser
{
    [BsonId]
    public string Id { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? EmailVerifiedAtUtc { get; set; }
}
