namespace JIssWeb.Model.Api.Services;

public interface IForumRateLimitBackend
{
    /// <summary>Returns true if the key would exceed the limit without consuming a slot.</summary>
    Task<bool> WouldExceedAsync(string key, int max, int windowSeconds);

    /// <summary>Unconditionally records one successful request for the key.</summary>
    Task RecordAsync(string key, int windowSeconds);

    /// <summary>Atomically checks and consumes one slot. Returns false if the limit is already reached.</summary>
    Task<bool> TryConsumeAsync(string key, int max, int windowSeconds);
}
