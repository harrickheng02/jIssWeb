using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场指认记录归档（G8/D3）：已结算（settledAtUtc 存在）且距今 ≥ 保留天数（默认 21，留 ≥14 余量）的指认记录清理。
/// 仅清理结算完成后的记录，绝不早于结算完成（用例 22 P0）。dedupe 仍由其自身 7d TTL 管理，本服务不触碰。
/// </summary>
public interface IArenaAccusationArchiveService
{
    /// <summary>删除 settledAtUtc 存在且 ≤ cutoffUtc 的指认记录，返回删除条数。</summary>
    Task<long> PurgeAsync(DateTime cutoffUtc, CancellationToken ct = default);
}

public sealed class ArenaAccusationArchiveService : IArenaAccusationArchiveService
{
    private readonly IMongoCollection<ArenaAccusationRecord> _accusations;
    private readonly ILogger<ArenaAccusationArchiveService> _logger;

    public ArenaAccusationArchiveService(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ILogger<ArenaAccusationArchiveService> logger)
    {
        _accusations = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName)
            .GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);
        _logger = logger;
    }

    public async Task<long> PurgeAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        var filter = Builders<ArenaAccusationRecord>.Filter.Exists(x => x.SettledAtUtc)
                     & Builders<ArenaAccusationRecord>.Filter.Lte(x => x.SettledAtUtc, cutoffUtc);
        var result = await _accusations.DeleteManyAsync(filter, ct);
        if (result.DeletedCount > 0)
            _logger.LogInformation("Arena accusation archive purged {Count} records settled on/before {Cutoff}.", result.DeletedCount, cutoffUtc);
        return result.DeletedCount;
    }
}
