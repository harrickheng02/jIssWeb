namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 静默指认写路径服务。严格限定为：去重原子插入 → 业务记录 pending → 计数 $inc CAS → 统一 200。
/// 不跨服务查询 accountType、不写积分事件、不发通知、不判 hit/miss、不标记待揭露（D4 零身份判定）。
/// </summary>
public interface IArenaAccusationService
{
    Task AccuseAsync(string accuserId, string targetUserId, CancellationToken cancellationToken = default);
}
