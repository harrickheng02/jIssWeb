using JIssWeb.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumAuthorDisplayResolver
{
    private readonly IMongoCollection<BsonDocument>? _profiles;
    private readonly ILogger<ForumAuthorDisplayResolver> _logger;

    public ForumAuthorDisplayResolver(
        IMongoClient client,
        IOptions<MongoSettings> opts,
        ILogger<ForumAuthorDisplayResolver> logger)
    {
        _logger = logger;
        var name = opts.Value.CustomerDatabaseName?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        _profiles = client.GetDatabase(name).GetCollection<BsonDocument>("profiles");
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> subs,
        CancellationToken ct = default)
    {
        var distinct = subs.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (distinct.Count == 0)
            return new Dictionary<string, string>();

        if (_profiles is null)
            return distinct.ToDictionary(s => s, ForumDisplayName.ForSub, StringComparer.OrdinalIgnoreCase);

        try
        {
            var f = Builders<BsonDocument>.Filter;
            var filter = f.Or(f.In("ownerUserId", distinct), f.In("OwnerUserId", distinct));
            var docs = await _profiles.Find(filter).ToListAsync(ct);
            var map = distinct.ToDictionary(s => s, ForumDisplayName.ForSub, StringComparer.OrdinalIgnoreCase);
            foreach (var d in docs)
            {
                var owner = TryGetOwnerId(d);
                if (string.IsNullOrEmpty(owner)) continue;
                var subKey = distinct.FirstOrDefault(s => string.Equals(s, owner, StringComparison.OrdinalIgnoreCase));
                if (subKey is null) continue;
                var nick = TryGetNickname(d);
                map[subKey] = string.IsNullOrWhiteSpace(nick) ? ForumDisplayName.ForSub(subKey) : nick.Trim();
            }

            return map;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer profiles lookup failed; using default display names.");
            return distinct.ToDictionary(s => s, ForumDisplayName.ForSub, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? TryGetOwnerId(BsonDocument d)
    {
        if (d.TryGetValue("ownerUserId", out var v) && v.IsString)
            return v.AsString;
        if (d.TryGetValue("OwnerUserId", out v) && v.IsString)
            return v.AsString;
        return null;
    }

    private static string? TryGetNickname(BsonDocument d)
    {
        if (d.TryGetValue("nickname", out var v))
        {
            if (v.IsString) return v.AsString;
        }

        if (d.TryGetValue("Nickname", out v))
        {
            if (v.IsString) return v.AsString;
        }

        return null;
    }
}
