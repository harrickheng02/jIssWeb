using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 后台定时服务：每 24 小时扫描一次，将已软删除且超过保留期的帖子与回复物理清除。
/// 帖子清除前会检查是否存在 Pending 举报，若存在则跳过并记录警告。
/// </summary>
public sealed class DraftCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DraftCleanupBackgroundService> _logger;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public DraftCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DraftCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 首次启动先等待 5 分钟，避免给启动期造成压力
        await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgePassAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DraftCleanupBackgroundService purge pass failed.");
            }

            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PurgePassAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var dbName = scope.ServiceProvider.GetRequiredService<IOptions<MongoSettings>>().Value.DatabaseName;
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<ForumSoftDeleteOptions>>().Value;

        var db = mongo.GetDatabase(dbName);
        var retentionDays = opts.RetentionDays < 1 ? 1 : opts.RetentionDays;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var postsDeleted = await PurgePostsAsync(db, cutoff, ct).ConfigureAwait(false);
        var repliesDeleted = await PurgeRepliesAsync(db, cutoff, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "DraftCleanup purge pass done: removed {Posts} post(s) and {Replies} reply/replies with DeletedAtUtc older than {Cutoff:O} (RetentionDays={Days}).",
            postsDeleted,
            repliesDeleted,
            cutoff,
            retentionDays);
    }

    /// <summary>
    /// 物理删除已软删且超保留期的帖子，并级联清理关联数据。
    /// 存在 Pending 举报的帖子会被跳过并记录警告。
    /// </summary>
    private async Task<long> PurgePostsAsync(IMongoDatabase db, DateTime cutoff, CancellationToken ct)
    {
        var posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        var reports = db.GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);

        // 1. 查找符合条件的帖子 Id
        var candidateFilter = Builders<ForumPostRecord>.Filter.And(
            Builders<ForumPostRecord>.Filter.Eq(p => p.State, "deleted"),
            Builders<ForumPostRecord>.Filter.Lt(p => p.DeletedAtUtc, cutoff));

        var candidateIds = await posts
            .Find(candidateFilter)
            .Project(p => p.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidateIds.Count == 0)
            return 0;

        // 2. 过滤掉存在 Pending 举报的帖子
        var pendingReportFilter = Builders<ForumReportRecord>.Filter.And(
            Builders<ForumReportRecord>.Filter.In(r => r.PostId, candidateIds),
            Builders<ForumReportRecord>.Filter.Eq(r => r.Status, ForumReportStatuses.Pending));

        var blockedPostIds = await reports
            .Find(pendingReportFilter)
            .Project(r => r.PostId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var blockedSet = new HashSet<string>(blockedPostIds, StringComparer.Ordinal);
        if (blockedSet.Count > 0)
        {
            _logger.LogWarning(
                "DraftCleanup skipping {Count} post(s) with open Pending reports: [{PostIds}]",
                blockedSet.Count,
                string.Join(", ", blockedSet));
        }

        var purgeIds = candidateIds.Where(id => !blockedSet.Contains(id)).ToList();
        if (purgeIds.Count == 0)
            return 0;

        // 3. 级联删除关联数据
        var purgeIdFilter = Builders<ForumReplyRecord>.Filter.In(r => r.PostId, purgeIds);

        await db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName)
            .DeleteManyAsync(purgeIdFilter, ct)
            .ConfigureAwait(false);

        await db.GetCollection<ForumPostLikeRecord>(ForumMongoSetup.LikesCollectionName)
            .DeleteManyAsync(Builders<ForumPostLikeRecord>.Filter.In(l => l.PostId, purgeIds), ct)
            .ConfigureAwait(false);

        await db.GetCollection<ForumPostFavoriteRecord>(ForumMongoSetup.FavoritesCollectionName)
            .DeleteManyAsync(Builders<ForumPostFavoriteRecord>.Filter.In(f => f.PostId, purgeIds), ct)
            .ConfigureAwait(false);

        await db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName)
            .DeleteManyAsync(Builders<InAppNotificationRecord>.Filter.In(n => n.PostId, purgeIds), ct)
            .ConfigureAwait(false);

        // 4. 物理删除帖子本身
        var result = await posts
            .DeleteManyAsync(Builders<ForumPostRecord>.Filter.In(p => p.Id, purgeIds), ct)
            .ConfigureAwait(false);

        return result.DeletedCount;
    }

    /// <summary>物理删除已软删且超保留期的回复（回复无举报跳过逻辑）。</summary>
    private async Task<long> PurgeRepliesAsync(IMongoDatabase db, DateTime cutoff, CancellationToken ct)
    {
        var replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);

        var filter = Builders<ForumReplyRecord>.Filter.And(
            Builders<ForumReplyRecord>.Filter.Eq(r => r.State, "deleted"),
            Builders<ForumReplyRecord>.Filter.Lt(r => r.DeletedAtUtc, cutoff));

        var result = await replies.DeleteManyAsync(filter, ct).ConfigureAwait(false);
        return result.DeletedCount;
    }
}
