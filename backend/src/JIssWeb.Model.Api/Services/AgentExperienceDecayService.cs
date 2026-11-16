using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// #38 经验权重月度衰减（后台服务）。每月 1 日：全部条目 weight × DecayFactor；
/// 「近期被继承且来源高存活」的条目额外 × SurvivalBonusFactor。按 yyyy-MM 唯一幂等。
/// 参照 Accounting.Api ArenaPointsDecayService 范式：PeriodicTimer(24h) + 启动补跑缺失月份。
/// </summary>
public sealed class AgentExperienceDecayService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IMongoCollection<AgentExperienceRecord> _experiences;
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<AgentExperienceDecayState> _runs;
    private readonly AgentExperienceInheritanceOptions _options;
    private readonly ILogger<AgentExperienceDecayService> _logger;

    public AgentExperienceDecayService(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions,
        IOptions<AgentExperienceInheritanceOptions> options,
        ILogger<AgentExperienceDecayService> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _experiences = db.GetCollection<AgentExperienceRecord>(AgentMongoSetup.ExperiencesCollectionName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _runs = db.GetCollection<AgentExperienceDecayState>(AgentMongoSetup.ExperienceDecayStateCollectionName);
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunPendingMonthsAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunPendingMonthsAsync(stoppingToken);
    }

    public async Task RunPendingMonthsAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return;
        var current = YearMonthString(DateTime.UtcNow);
        var latest = await LatestRunMonthAsync(ct);
        var ym = latest is null ? current : AddYearMonth(latest, 1);
        while (string.Compare(ym, current, StringComparison.Ordinal) <= 0)
        {
            await RunMonthAsync(ym, ct);
            ym = AddYearMonth(ym, 1);
        }
    }

    public async Task RunMonthAsync(string yearMonth, CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        // 幂等认领：yyyy-MM 唯一；重复执行 11000 静默跳过。
        try
        {
            await _runs.InsertOneAsync(new AgentExperienceDecayState { YearMonth = yearMonth }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return;
        }

        var all = await _experiences.Find(FilterDefinition<AgentExperienceRecord>.Empty).ToListAsync(ct);
        if (all.Count == 0) return;

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.RecentInheritDays));
        // 高存活来源 persona id 集合（agentUserId）：存活 ≥ NormalizeDays 或已获图灵奖章（eliminated 奖章账号）。
        var highSurvivorIds = (await _personas.Find(FilterDefinition<AgentPersonaRecord>.Empty).ToListAsync(ct))
            .Where(p => p.SurvivalDays >= Math.Max(1, _options.NormalizeDays) || p.State == AgentPersonaState.Eliminated)
            .Select(p => p.AgentUserId)
            .ToHashSet(StringComparer.Ordinal);

        // 奖励条件（spec）：条目「近期被继承」（LastInheritedAtUtc 在时间窗内）且「来源高存活」（SourcePersonaId 命中高存活集合）。
        var bonusIds = all
            .Where(e => e.LastInheritedAtUtc >= cutoff && highSurvivorIds.Contains(e.SourcePersonaId))
            .Select(e => e.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var e in all)
        {
            var factor = _options.DecayFactor * (bonusIds.Contains(e.Id) ? Math.Max(1.0, _options.SurvivalBonusFactor) : 1.0);
            await _experiences.UpdateOneAsync(
                x => x.Id == e.Id,
                Builders<AgentExperienceRecord>.Update.Mul(x => x.Weight, factor),
                cancellationToken: ct);
        }
        _logger.LogInformation("Experience decay applied for {Month}: {Count} entries, {Bonus} bonused.", yearMonth, all.Count, bonusIds.Count);
    }

    private async Task<string?> LatestRunMonthAsync(CancellationToken ct)
    {
        var last = await _runs.Find(FilterDefinition<AgentExperienceDecayState>.Empty)
            .Sort(Builders<AgentExperienceDecayState>.Sort.Descending(x => x.YearMonth))
            .FirstOrDefaultAsync(ct);
        return last?.YearMonth;
    }

    public static string YearMonthString(DateTime dt) => $"{dt.Year:D4}-{dt.Month:D2}";

    public static string AddYearMonth(string ym, int months)
    {
        var parts = ym.Split('-');
        var total = int.Parse(parts[0]) * 12 + (int.Parse(parts[1]) - 1) + months;
        return $"{total / 12:D4}-{total % 12 + 1:D2}";
    }
}
