using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace JIssWeb.Accounting.Api.Models;

/// <summary>
/// 每用户积分余额快照（快查）。余额永不为负（下限 0）。
/// </summary>
public class UserPointsLedgerRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string UserId { get; set; } = "";
    public int Balance { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 只追加积分流水。记录类型/金额/发生后余额/时间。
/// 严守隐私红线：SHALL NOT 含 eventId 或 targetUserId（eventId 内嵌 targetUserId）。
/// </summary>
public class UserPointsTransactionRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string TxId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "";
    public int Delta { get; set; }
    public int BalanceAfter { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 消费去重集合（内部）。仅存 eventId 唯一键，用于消费者幂等（重放/崩溃续跑不重复计）。
/// 绝不序列化到 API 响应。
/// </summary>
public class ArenaPointsConsumedEventRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string EventId { get; set; } = "";
}

/// <summary>
/// 消费游标（单例文档）。记录最后处理的事件 _id（ObjectId 字符串，按时间序），用于高效分页扫描。
/// 正确性由 ArenaPointsConsumedEventRecord 唯一索引保证，本游标仅为性能优化。
/// </summary>
public class ArenaPointsConsumerStateRecord
{
    public string Id { get; set; } = "singleton";
    public string LastProcessedId { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 月度衰减幂等（按 yyyy-MM 唯一）。
/// </summary>
public class ArenaDecayRunRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string YearMonth { get; set; } = "";
    public DateTime RunAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// AIQI / 净流量周报幂等（按 weekKey 唯一）。
/// </summary>
public class ArenaMetricRunRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string WeekKey { get; set; } = "";
    public DateTime RunAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// AIQI 计算结果（运营仪表盘数据源）。
/// </summary>
public class ArenaAiqiResultRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string WeekKey { get; set; } = "";
    public double Aiqi { get; set; }
    public double DetectionSuccessRate { get; set; }
    public int TotalAccusations { get; set; }
    public int ActiveUsers { get; set; }
    public bool SampleSufficient { get; set; }
    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 净流量周报结果（运营仪表盘数据源）。
/// </summary>
public class ArenaNetflowResultRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string WeekKey { get; set; } = "";
    public int NetFlow { get; set; }
    public bool InflationAlert { get; set; }
    public bool WarmupPackIssued { get; set; }
    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 侦探技能库存（一次性消耗品）。每次购买 = 一行；death_shield 核销时 Consumed=true。
/// </summary>
public class UserSkillInventoryRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string UserId { get; set; } = "";
    public string SkillId { get; set; } = "";
    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Consumed { get; set; }
}
