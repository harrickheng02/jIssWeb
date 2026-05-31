namespace JIssWeb.Model.Api.Services;

public sealed class ForumSanctionStatusResult
{
    public bool IsMuted { get; set; }
    public DateTime? MutedUntilUtc { get; set; }
}

public interface IUserSanctionClient
{
    Task<ForumSanctionStatusResult> GetForumSanctionStatusAsync(string sub, CancellationToken ct = default);

    Task<UserSanctionCreatedResult?> CreateSanctionAsync(
        string sub,
        string type,
        string reason,
        string operatorSub,
        string? reportId,
        string? durationPreset,
        CancellationToken ct = default);

    Task<bool> RevokeMuteAsync(
        string sub,
        string sanctionId,
        string revokedBySub,
        string revokeReason,
        CancellationToken ct = default);
}

public sealed class UserSanctionCreatedResult
{
    public string SanctionId { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DurationPreset { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
