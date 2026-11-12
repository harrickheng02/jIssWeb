using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumAuthorDisplayResolver
{
    private readonly IMongoCollection<BsonDocument>? _profiles;
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly ILogger<ForumAuthorDisplayResolver> _logger;

    public ForumAuthorDisplayResolver(
        IMongoClient client,
        IOptions<MongoSettings> opts,
        ILogger<ForumAuthorDisplayResolver> logger)
    {
        _logger = logger;
        var db = client.GetDatabase(opts.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
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

        var map = distinct.ToDictionary(s => s, ForumDisplayName.ForSub, StringComparer.OrdinalIgnoreCase);

        if (_profiles is not null)
        {
            try
            {
                var f = Builders<BsonDocument>.Filter;
                var filter = f.Or(f.In("ownerUserId", distinct), f.In("OwnerUserId", distinct));
                var docs = await _profiles.Find(filter).ToListAsync(ct);
                foreach (var d in docs)
                {
                    var owner = TryGetOwnerId(d);
                    if (string.IsNullOrEmpty(owner)) continue;
                    var subKey = distinct.FirstOrDefault(s => string.Equals(s, owner, StringComparison.OrdinalIgnoreCase));
                    if (subKey is null) continue;
                    var nick = TryGetNickname(d);
                    if (!string.IsNullOrWhiteSpace(nick))
                        map[subKey] = nick.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Customer profiles lookup failed; continuing with fallbacks.");
            }
        }

        try
        {
            var personas = await _personas.Find(x => distinct.Contains(x.Id)).ToListAsync(ct);
            foreach (var persona in personas)
            {
                var nick = persona.Nickname?.Trim();
                if (string.IsNullOrWhiteSpace(nick)) continue;
                map[persona.Id] = nick;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent persona nickname lookup failed; keeping prior display names.");
        }

        return map;
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
        if (d.TryGetValue("nickname", out var v) && v.IsString)
            return v.AsString;
        if (d.TryGetValue("Nickname", out v) && v.IsString)
            return v.AsString;
        return null;
    }
}
