using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>待判定内容。只携带判定所需的最小字段，不含屏蔽词命中信息。</summary>
public sealed record ModerationRequest(
    string TargetType, string TargetId, string PostId, string BoardId, string AuthorSubId,
    string? Title, string Body);

/// <summary>
/// 门禁结论。<see cref="State"/> 是内容最终应写入的 State（published / pending_review），
/// <see cref="Enqueued"/> 表示是否已写入复审队列。
/// </summary>
public sealed record ModerationDecision(string State, bool Enqueued, ModerationVerdict? Verdict = null)
{
    public static readonly ModerationDecision Pass = new(ForumContentStates.Published, false);
}

public interface IContentModerationGate
{
    Task<ModerationDecision> EvaluateAsync(ModerationRequest request, CancellationToken ct = default);
}

/// <summary>
/// AI 版主阶段一内容门禁：开关 → 日上限 → 分类（带超时）→ 三档处置 → 入队。
/// 任何异常或超时一律 fail-open，返回 Pass。
/// </summary>
public sealed class ContentModerationGate : IContentModerationGate
{
    private readonly IMongoCollection<ForumModerationQueueRecord> _queue;
    private readonly IMongoCollection<ForumModerationDailyCallRecord> _dailyCalls;
    private readonly IContentModerator _moderator;
    private readonly IDuplicateContentDetector? _duplicates;
    private readonly ForumContentModerationOptions _options;
    private readonly ILogger<ContentModerationGate> _logger;

    public ContentModerationGate(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions,
        IContentModerator moderator,
        IOptions<ForumContentModerationOptions> options,
        ILogger<ContentModerationGate> logger,
        IDuplicateContentDetector? duplicates = null)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _queue = db.GetCollection<ForumModerationQueueRecord>(ForumMongoSetup.ModerationQueueCollectionName);
        _dailyCalls = db.GetCollection<ForumModerationDailyCallRecord>(ForumMongoSetup.ModerationDailyCallsCollectionName);
        _moderator = moderator;
        _duplicates = duplicates;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ModerationDecision> EvaluateAsync(ModerationRequest request, CancellationToken ct = default)
    {
        if (!_options.Enabled) return ModerationDecision.Pass;

        try
        {
            if (!await TryConsumeQuotaAsync(ct))
            {
                _logger.LogWarning("Content moderation daily cap {Cap} reached; degrading to pass.", _options.DailyLlmCallCap);
                return ModerationDecision.Pass;
            }

            var verdict = await ClassifyWithTimeoutAsync(request, ct);
            if (verdict is null) return ModerationDecision.Pass;

            if (verdict.IsViolation)
            {
                var autoHide = verdict.Confidence >= _options.AutoHideConfidence
                               && _options.AutoHideVerdicts.Contains(verdict.Verdict, StringComparer.OrdinalIgnoreCase);

                if (autoHide)
                {
                    await EnqueueAsync(request, verdict, ForumModerationQueueStatuses.Pending, ct);
                    return new ModerationDecision(ForumContentStates.PendingReview, true, verdict);
                }

                if (verdict.Confidence >= _options.FlagConfidence)
                {
                    await EnqueueAsync(request, verdict, ForumModerationQueueStatuses.Flagged, ct);
                    return new ModerationDecision(ForumContentStates.Published, true, verdict);
                }
            }

            // LLM 未命中时用零成本哈希兜底：同作者窗口内重复内容只标记、不隐藏。
            if (_duplicates is not null && await _duplicates.DetectAndRecordAsync(request, ct))
            {
                await EnqueueAsync(request, verdict, ForumModerationQueueStatuses.Flagged, ct, ForumModerationSources.Duplicate);
                return new ModerationDecision(ForumContentStates.Published, true, verdict);
            }

            return new ModerationDecision(ForumContentStates.Published, false, verdict);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content moderation gate failed for {TargetType}/{TargetId}; fail-open.", request.TargetType, request.TargetId);
            return ModerationDecision.Pass;
        }
    }

    private async Task<ModerationVerdict?> ClassifyWithTimeoutAsync(ModerationRequest request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Math.Max(1, _options.TimeoutMs));
        try
        {
            return await _moderator.ClassifyAsync(request.Title, request.Body, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("Content moderation timed out after {Timeout}ms; fail-open.", _options.TimeoutMs);
            return null;
        }
    }

    private async Task EnqueueAsync(
        ModerationRequest request, ModerationVerdict verdict, string status, CancellationToken ct,
        string source = ForumModerationSources.Llm)
    {
        var record = new ForumModerationQueueRecord
        {
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            PostId = request.PostId,
            BoardId = request.BoardId,
            AuthorSubId = request.AuthorSubId,
            Source = source,
            LlmProfileId = verdict.LlmProfileId,
            Status = status,
            Verdict = verdict.Verdict,
            Confidence = verdict.Confidence,
            Reason = verdict.Reason,
            CreatedAtUtc = DateTime.UtcNow,
        };
        try
        {
            await _queue.InsertOneAsync(record, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // 幂等：同一内容同一来源已入队，静默跳过。
        }
    }

    /// <summary>日上限按 UTC 自然日计数，落在独立集合，跨进程重启仍生效。</summary>
    private async Task<bool> TryConsumeQuotaAsync(CancellationToken ct)
    {
        if (_options.DailyLlmCallCap <= 0) return true;

        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var filter = Builders<ForumModerationDailyCallRecord>.Filter.Eq(x => x.Day, day);
        var existing = await _dailyCalls.Find(filter).FirstOrDefaultAsync(ct);
        if (existing is not null && existing.Count >= _options.DailyLlmCallCap) return false;

        await _dailyCalls.UpdateOneAsync(
            filter,
            Builders<ForumModerationDailyCallRecord>.Update
                .Inc(x => x.Count, 1)
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString()),
            new UpdateOptions { IsUpsert = true },
            ct);
        return true;
    }
}
