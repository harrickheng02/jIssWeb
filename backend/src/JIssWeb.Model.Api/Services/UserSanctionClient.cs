using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JIssWeb.Common;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

public sealed class UserSanctionClient : IUserSanctionClient
{
    internal const string InternalApiKeyHeader = "X-JIssWeb-Internal-Key";
    private static readonly TimeSpan LocalCacheTtl = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserSanctionClient> _logger;

    public UserSanctionClient(
        HttpClient http,
        IOptions<InternalServiceOptions> internalOptions,
        IMemoryCache cache,
        ILogger<UserSanctionClient> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        var key = internalOptions.Value.ApiKey?.Trim() ?? "";
        if (key.Length > 0)
            _http.DefaultRequestHeaders.TryAddWithoutValidation(InternalApiKeyHeader, key);
    }

    public async Task<ForumSanctionStatusResult> GetForumSanctionStatusAsync(string sub, CancellationToken ct = default)
    {
        var cacheKey = $"model-forum-sanction:{sub}";
        if (_cache.TryGetValue(cacheKey, out ForumSanctionStatusResult? cached) && cached is not null)
            return cached;

        try
        {
            var res = await _http.GetFromJsonAsync<ApiResult<RemoteForumSanctionStatusDto>>(
                $"/api/internal/users/{Uri.EscapeDataString(sub)}/forum-sanction-status",
                ct);

            var dto = res?.Data;
            var mapped = new ForumSanctionStatusResult
            {
                IsMuted = dto?.IsMuted ?? false,
                MutedUntilUtc = dto?.MutedUntilUtc,
            };
            _cache.Set(cacheKey, mapped, LocalCacheTtl);
            return mapped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query forum sanction status for {Sub}; allowing write", sub);
            return new ForumSanctionStatusResult { IsMuted = false };
        }
    }

    public async Task<UserSanctionCreatedResult?> CreateSanctionAsync(
        string sub,
        string type,
        string reason,
        string operatorSub,
        string? reportId,
        string? durationPreset,
        CancellationToken ct = default)
    {
        var body = new
        {
            type,
            reason,
            operatorSub,
            reportId,
            durationPreset,
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"/api/internal/users/{Uri.EscapeDataString(sub)}/sanctions",
                body,
                ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var envelope = await response.Content.ReadFromJsonAsync<ApiResult<RemoteSanctionCreatedDto>>(cancellationToken: ct);
            if (envelope?.Data is null)
                return null;

            _cache.Remove($"model-forum-sanction:{sub}");
            return new UserSanctionCreatedResult
            {
                SanctionId = envelope.Data.SanctionId,
                Type = envelope.Data.Type,
                DurationPreset = envelope.Data.DurationPreset,
                ExpiresAtUtc = envelope.Data.ExpiresAtUtc,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create forum sanction for {Sub}", sub);
            return null;
        }
    }

    public async Task<bool> RevokeMuteAsync(
        string sub,
        string sanctionId,
        string revokedBySub,
        string revokeReason,
        CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(
            $"/api/internal/users/{Uri.EscapeDataString(sub)}/sanctions/{Uri.EscapeDataString(sanctionId)}/revoke",
            new { revokedBySub, revokeReason },
            ct);

        if (response.IsSuccessStatusCode)
            _cache.Remove($"model-forum-sanction:{sub}");

        return response.IsSuccessStatusCode;
    }

    private sealed class RemoteForumSanctionStatusDto
    {
        [JsonPropertyName("isMuted")]
        public bool IsMuted { get; set; }

        [JsonPropertyName("mutedUntilUtc")]
        public DateTime? MutedUntilUtc { get; set; }
    }

    private sealed class RemoteSanctionCreatedDto
    {
        [JsonPropertyName("sanctionId")]
        public string SanctionId { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("durationPreset")]
        public string? DurationPreset { get; set; }

        [JsonPropertyName("expiresAtUtc")]
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
