namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场积分事件幂等 id 派生。由 (weekId, userId, targetUserId, type) 确定性拼接，
/// 保证重放恰好一次（D9）。targetUserId 仅供 #35 核对 / 幂等，绝不出现在 API / 通知 / 日志。
/// </summary>
public static class ArenaEventId
{
    public static string Derive(string weekId, string userId, string targetUserId, string type)
        => $"{weekId}|{userId}|{targetUserId}|{type}";
}
