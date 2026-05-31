using JIssWeb.Common.Options;
using JIssWeb.User.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.User.Api.Services;

public sealed class UserSanctionService
{
    private static readonly TimeSpan StatusCacheTtl = TimeSpan.FromMinutes(5);

    private readonly IMongoCollection<UserSanctionRecord> _sanctions;
    private readonly IMemoryCache _cache;

    public UserSanctionService(IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions, IMemoryCache cache)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _sanctions = db.GetCollection<UserSanctionRecord>("user_sanctions");
        _cache = cache;
    }

    public async Task<ForumSanctionStatusDto> GetForumSanctionStatusAsync(string sub, CancellationToken ct = default)
    {
        var key = CacheKey(sub);
        if (_cache.TryGetValue(key, out ForumSanctionStatusDto? cached) && cached is not null)
            return cached;

        var now = DateTime.UtcNow;
        var records = await _sanctions
            .Find(x => x.Sub == sub)
            .SortByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var activeMute = records
            .Where(x => x.Type == UserSanctionTypes.Mute && x.RevokedAtUtc is null && x.ExpiresAtUtc is not null && now < x.ExpiresAtUtc)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefault();

        var warningCount = records.Count(x =>
            x.Type == UserSanctionTypes.Warning && x.RevokedAtUtc is null);

        var dto = new ForumSanctionStatusDto
        {
            IsMuted = activeMute is not null,
            MutedUntilUtc = activeMute?.ExpiresAtUtc,
            ActiveWarningCount = warningCount,
        };

        _cache.Set(key, dto, StatusCacheTtl);
        return dto;
    }

    public async Task<UserSanctionRecord> CreateAsync(
        string sub,
        string type,
        string reason,
        string operatorSub,
        string? reportId,
        string? durationPreset,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        DateTime? expires = null;
        string? storedPreset = null;

        if (type == UserSanctionTypes.Mute)
        {
            if (!UserSanctionDurationPresets.TryParse(durationPreset ?? UserSanctionDurationPresets.Hours24, out storedPreset))
                throw new ArgumentException("INVALID_DURATION_PRESET");

            expires = UserSanctionDurationPresets.ComputeExpiresUtc(now, storedPreset);
        }

        var record = new UserSanctionRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Sub = sub,
            Type = type,
            Reason = reason,
            OperatorSub = operatorSub,
            ReportId = string.IsNullOrWhiteSpace(reportId) ? null : reportId.Trim(),
            DurationPreset = storedPreset,
            StartsAtUtc = now,
            ExpiresAtUtc = expires,
            CreatedAtUtc = now,
        };

        await _sanctions.InsertOneAsync(record, cancellationToken: ct);
        Invalidate(sub);
        return record;
    }

    public async Task<UserSanctionRecord?> RevokeMuteAsync(
        string sub,
        string sanctionId,
        string revokedBySub,
        string revokeReason,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<UserSanctionRecord>.Filter.And(
            Builders<UserSanctionRecord>.Filter.Eq(x => x.Id, sanctionId),
            Builders<UserSanctionRecord>.Filter.Eq(x => x.Sub, sub),
            Builders<UserSanctionRecord>.Filter.Eq(x => x.Type, UserSanctionTypes.Mute),
            Builders<UserSanctionRecord>.Filter.Eq(x => x.RevokedAtUtc, null));

        var update = Builders<UserSanctionRecord>.Update
            .Set(x => x.RevokedAtUtc, now)
            .Set(x => x.RevokedBySub, revokedBySub)
            .Set(x => x.RevokeReason, revokeReason);

        var options = new FindOneAndUpdateOptions<UserSanctionRecord> { ReturnDocument = ReturnDocument.After };
        var updated = await _sanctions.FindOneAndUpdateAsync(filter, update, options, ct);
        if (updated is not null)
            Invalidate(sub);
        return updated;
    }

    public void Invalidate(string sub) => _cache.Remove(CacheKey(sub));

    private static string CacheKey(string sub) => $"forum-sanction-status:{sub}";
}

public class ForumSanctionStatusDto
{
    public bool IsMuted { get; set; }
    public DateTime? MutedUntilUtc { get; set; }
    public int ActiveWarningCount { get; set; }
}
