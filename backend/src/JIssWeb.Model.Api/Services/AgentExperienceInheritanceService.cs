using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// #38 继承注入服务：新建 Persona 时按 weight 降序选 TopN 经验、回写 inheritedBy，
/// 并从 inheritedFrom 来源计算 generation = max(source.generation) + 1。
/// 开关关闭时仅做 generation 赋值（血缘为固有属性），不做经验选择/回写。
/// </summary>
public interface IExperienceInheritanceService
{
    /// <summary>把继承结果与世代赋值写入 record（原地修改），并异步回写选中条目的 inheritedBy。</summary>
    Task ApplyAsync(AgentPersonaRecord record, IReadOnlyList<string>? inheritedFrom = null, CancellationToken ct = default);

    /// <summary>供调度器构建 prompt：按 id 批量读取已继承条目。</summary>
    Task<IReadOnlyList<AgentExperienceRecord>> LoadAsync(IReadOnlyList<string> experienceIds, CancellationToken ct = default);
}

public sealed class ExperienceInheritanceService : IExperienceInheritanceService
{
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<AgentExperienceRecord> _experiences;
    private readonly AgentExperienceInheritanceOptions _options;

    public ExperienceInheritanceService(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions,
        IOptions<AgentExperienceInheritanceOptions> options)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _experiences = db.GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName);
        _options = options.Value;
    }

    public async Task ApplyAsync(AgentPersonaRecord record, IReadOnlyList<string>? inheritedFrom = null, CancellationToken ct = default)
    {
        // 世代赋值（固有血缘，与开关无关）：来源 persona 用 personaId 或 agentUserId 匹配。
        var sources = (inheritedFrom ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Where(s => s != record.PersonaId && s != record.AgentUserId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var validSources = new List<string>();
        var maxGen = 1;
        if (sources.Count > 0)
        {
            var found = await _personas.Find(p => sources.Contains(p.PersonaId) || sources.Contains(p.AgentUserId)).ToListAsync(ct);
            foreach (var s in sources)
            {
                var src = found.FirstOrDefault(p => p.PersonaId == s || p.AgentUserId == s);
                if (src is null) continue;
                validSources.Add(s);
                maxGen = Math.Max(maxGen, src.Generation);
            }
        }
        record.InheritedFrom = validSources;
        record.Generation = validSources.Count == 0 ? 1 : maxGen + 1;

        if (!_options.Enabled) return;

        var top = await _experiences.Find(FilterDefinition<AgentExperienceRecord>.Empty)
            .SortByDescending(x => x.Weight)
            .Limit(Math.Max(0, _options.TopN))
            .ToListAsync(ct);
        record.ExperienceIds = top.Select(x => x.ExperienceId).ToList();
        var now = DateTime.UtcNow;
        foreach (var e in top)
        {
            await _experiences.UpdateOneAsync(
                x => x.Id == e.Id && !x.InheritedBy.Contains(record.AgentUserId),
                Builders<AgentExperienceRecord>.Update
                    .AddToSet(x => x.InheritedBy, record.AgentUserId)
                    .Set(x => x.LastInheritedAtUtc, now),
                cancellationToken: ct);
        }
    }

    public async Task<IReadOnlyList<AgentExperienceRecord>> LoadAsync(IReadOnlyList<string> experienceIds, CancellationToken ct = default)
    {
        if (experienceIds is null || experienceIds.Count == 0) return Array.Empty<AgentExperienceRecord>();
        try
        {
            return await _experiences.Find(x => experienceIds.Contains(x.ExperienceId)).ToListAsync(ct);
        }
        catch (Exception)
        {
            return Array.Empty<AgentExperienceRecord>();
        }
    }
}
