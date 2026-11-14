using System.Collections.Concurrent;
using MongoDB.Driver;
using JIssWeb.Report.Api.Models;

namespace JIssWeb.Report.Api.Services;

/// <summary>
/// 判定 sub 是否为 AI 智能体（agent_personas.AgentUserId == sub）。
/// 生产环境不签发 accountType claim，因此 AI vs 人类拆分统一走此 join 判定。
/// 内存缓存 + TTL 60s：agent 数量小，避免每次面板请求都查库。
/// </summary>
public sealed class IsAgentSubResolver
{
    private readonly IMongoDatabase _db;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, (bool IsAgent, DateTime CachedAt)> _cache = new();

    public IsAgentSubResolver([Microsoft.Extensions.DependencyInjection.FromKeyedServices("arena-dashboard-model")] IMongoDatabase db)
        => _db = db;

    public async Task<bool> IsAgentSubAsync(string sub, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sub)) return false;
        if (_cache.TryGetValue(sub, out var entry) && DateTime.UtcNow - entry.CachedAt < _ttl)
            return entry.IsAgent;

        var filter = Builders<DashboardAgentPersona>.Filter.Eq(x => x.AgentUserId, sub);
        var exists = await _db.GetCollection<DashboardAgentPersona>(DashboardCollectionNames.AgentPersonas)
            .Find(filter).Limit(1).AnyAsync(ct);
        _cache[sub] = (exists, DateTime.UtcNow);
        return exists;
    }
}
