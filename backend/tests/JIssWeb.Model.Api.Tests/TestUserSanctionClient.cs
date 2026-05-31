using System.Collections.Concurrent;
using JIssWeb.Model.Api.Services;

namespace JIssWeb.Model.Api.Tests;

/// <summary>In-memory IUserSanctionClient for Model.Api integration tests.</summary>
public sealed class TestUserSanctionClient : IUserSanctionClient
{
    private readonly ConcurrentDictionary<string, ForumSanctionStatusResult> _status = new();
    private readonly ConcurrentDictionary<string, List<UserSanctionCreatedResult>> _created = new();

    public void SetMuted(string sub, DateTime? untilUtc = null)
    {
        var until = untilUtc ?? DateTime.UtcNow.AddHours(24);
        _status[sub] = new ForumSanctionStatusResult { IsMuted = true, MutedUntilUtc = until };
    }

    public void ClearMute(string sub) =>
        _status[sub] = new ForumSanctionStatusResult { IsMuted = false };

    public Task<ForumSanctionStatusResult> GetForumSanctionStatusAsync(string sub, CancellationToken ct = default)
    {
        var hit = _status.GetValueOrDefault(sub) ?? new ForumSanctionStatusResult { IsMuted = false };
        return Task.FromResult(hit);
    }

    public Task<UserSanctionCreatedResult?> CreateSanctionAsync(
        string sub,
        string type,
        string reason,
        string operatorSub,
        string? reportId,
        string? durationPreset,
        CancellationToken ct = default)
    {
        var id = "sanction-" + Guid.NewGuid().ToString("N");
        DateTime? expires = null;
        if (type == "mute")
        {
            expires = durationPreset switch
            {
                "7d" => DateTime.UtcNow.AddDays(7),
                "30d" => DateTime.UtcNow.AddDays(30),
                _ => DateTime.UtcNow.AddHours(24),
            };
            _status[sub] = new ForumSanctionStatusResult { IsMuted = true, MutedUntilUtc = expires };
        }

        var result = new UserSanctionCreatedResult
        {
            SanctionId = id,
            Type = type,
            DurationPreset = durationPreset,
            ExpiresAtUtc = expires,
        };
        _created.AddOrUpdate(sub, _ => new List<UserSanctionCreatedResult> { result }, (_, list) =>
        {
            list.Add(result);
            return list;
        });
        return Task.FromResult<UserSanctionCreatedResult?>(result);
    }

    public Task<bool> RevokeMuteAsync(string sub, string sanctionId, string revokedBySub, string revokeReason, CancellationToken ct = default)
    {
        ClearMute(sub);
        return Task.FromResult(true);
    }
}
