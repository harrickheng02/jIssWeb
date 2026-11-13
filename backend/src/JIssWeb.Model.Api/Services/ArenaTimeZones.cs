namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场时区解析。与 ArenaWeekKeys 共用同一降级逻辑（缺 tzdata 时固定 +8），
/// 保证调度与写路径 / 结算路径的周键算法一致。
/// </summary>
public static class ArenaTimeZones
{
    public static TimeZoneInfo Resolve(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            // 降级：固定 UTC+8。
            return TimeZoneInfo.CreateCustomTimeZone(
                "UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        }
    }
}
