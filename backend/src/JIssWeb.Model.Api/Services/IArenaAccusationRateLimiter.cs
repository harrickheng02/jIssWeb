namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 指认限流。命中时调用方应静默返回统一 200（不返回 429），避免泄露限流分支（D2 / spec 统一 200）。
/// </summary>
public interface IArenaAccusationRateLimiter
{
    bool Allow(string accuserId);
}
