using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场单周揭露编排服务（G4 调度 + G5 prepare + G6 commit）。
/// 核心职责：
/// 1) weekKey 原子领取（reveal_jobs 唯一索引，11000 视为已领取 → 整批不重复跑，D13）；
/// 2) prepare：只读批量判 accountType（查 agent_personas）→ 不可变结算计划 → state=settled，零对外副作用；
/// 3) commit：写积分事件 → 更新 tally / persona → 生成自白帖（arena_pending）→ 批量 +50 通知
///    （uniq_eventId 幂等）→ 单 CAS 置 completed（唯一对外可见性门禁）→ 翻帖 + 释放通知；
/// 4) 批次状态机 settling → settled → revealing → completed 分阶段幂等，失败保留 in-progress 由续跑接管。
/// </summary>
public sealed class ArenaRevealService : IArenaRevealService
{
    private readonly IMongoCollection<ArenaRevealJobRecord> _jobs;
    private readonly IMongoCollection<ArenaSettlementPlanRecord> _plans;
    private readonly IMongoCollection<ArenaTargetTallyRecord> _tallies;
    private readonly IMongoCollection<ArenaAccusationRecord> _accusations;
    private readonly IMongoCollection<ArenaPointsEventRecord> _events;
    private readonly IMongoCollection<InAppNotificationRecord> _notifications;
    private readonly IMongoCollection<AgentPersonaRecord> _personas;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly ArenaRevealOptions _options;
    private readonly IArenaConfessionGenerator _confessionGenerator;
    private readonly IForumBlockedWordFilter _blockedWords;
    private readonly ILogger<ArenaRevealService> _logger;

    public ArenaRevealService(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        IOptions<ArenaRevealOptions> arenaOptions,
        IArenaConfessionGenerator confessionGenerator,
        IForumBlockedWordFilter blockedWords,
        ILogger<ArenaRevealService> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _jobs = db.GetCollection<ArenaRevealJobRecord>(ArenaMongoSetup.RevealJobsCollectionName);
        _plans = db.GetCollection<ArenaSettlementPlanRecord>(ArenaMongoSetup.SettlementPlansCollectionName);
        _tallies = db.GetCollection<ArenaTargetTallyRecord>(ArenaMongoSetup.TalliesCollectionName);
        _accusations = db.GetCollection<ArenaAccusationRecord>(ArenaMongoSetup.AccusationsCollectionName);
        _events = db.GetCollection<ArenaPointsEventRecord>(ArenaMongoSetup.PointsEventsCollectionName);
        _notifications = db.GetCollection<InAppNotificationRecord>(ForumMongoSetup.NotificationsCollectionName);
        _personas = db.GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _options = arenaOptions.Value;
        _confessionGenerator = confessionGenerator;
        _blockedWords = blockedWords;
        _logger = logger;
    }

    public async Task<ClaimOutcome> TryClaimWeekAsync(string weekKey, CancellationToken cancellationToken = default)
    {
        var job = new ArenaRevealJobRecord
        {
            WeekKey = weekKey,
            State = ArenaRevealJobState.Settling,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        try
        {
            await _jobs.InsertOneAsync(job, cancellationToken: cancellationToken);
            return ClaimOutcome.Owned;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // 唯一 weekKey 已被本实例或他实例领取 → 不重复处理（D13 多实例护盾）。
            return ClaimOutcome.NotOwned;
        }
    }

    public async Task<RevealOutcome> RevealWeekAsync(string weekKey, CancellationToken cancellationToken = default)
    {
        var claim = await TryClaimWeekAsync(weekKey, cancellationToken);
        if (claim != ClaimOutcome.Owned)
            return RevealOutcome.Skipped;

        try
        {
            var verdicts = await PrepareWeekAsync(weekKey, cancellationToken);
            await TransitionAsync(weekKey, ArenaRevealJobState.Settled, cancellationToken);
            await TransitionAsync(weekKey, ArenaRevealJobState.Revealing, cancellationToken);
            await CommitWeekAsync(weekKey, verdicts, cancellationToken);
            // 单 CAS：completed 是唯一的对外可见性门禁（D5）。仅当状态仍为 revealing 时翻转，保证恰好一次。
            var completed = await _jobs.UpdateOneAsync(
                Builders<ArenaRevealJobRecord>.Filter
                    .Eq(x => x.WeekKey, weekKey) & Builders<ArenaRevealJobRecord>.Filter.Eq(x => x.State, ArenaRevealJobState.Revealing),
                Builders<ArenaRevealJobRecord>.Update
                    .Set(x => x.State, ArenaRevealJobState.Completed)
                    .Set(x => x.PublishedAtUtc, DateTime.UtcNow)
                    .Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: cancellationToken);

            if (completed.MatchedCount == 0)
            {
                _logger.LogWarning("Arena reveal week {WeekKey} completed CAS 未命中（可能已由他实例完成），跳过翻帖/释放。", weekKey);
                return RevealOutcome.Processed;
            }

            // 门禁后处理：自白帖 arena_pending → published；批次通知置 ReleasedAtUtc（D5/D6）。
            await ReleaseWeekAsync(weekKey, cancellationToken);
            _logger.LogInformation("Arena reveal week {WeekKey} completed (ProcessedCount={Count}).", weekKey, verdicts.Count);
            return RevealOutcome.Processed;
        }
        catch (Exception ex)
        {
            // 保留 in-progress 状态（settling/settled/revealing），由后续续跑接管；不删除批次，避免重复领取竞争。
            _logger.LogError(ex, "Arena reveal week {WeekKey} pipeline failed; job left in-progress for retry.", weekKey);
            return RevealOutcome.Skipped;
        }
    }

    /// <summary>
    /// prepare：只读批量判 accountType → 不可变结算计划。零对外副作用。
    /// 批量查询失败则抛出异常 → 调用方不落盘、整批不结算、可从断点续跑（D5）。
    /// </summary>
    private async Task<List<ArenaSettlementVerdict>> PrepareWeekAsync(string weekKey, CancellationToken cancellationToken)
    {
        var thresholdReached = await _tallies.Find(Builders<ArenaTargetTallyRecord>.Filter
                .Eq(x => x.WeekKey, weekKey) & Builders<ArenaTargetTallyRecord>.Filter.Eq(x => x.Status, ArenaTallyStatus.ThresholdReached))
            .ToListAsync(cancellationToken);

        var verdicts = new List<ArenaSettlementVerdict>(thresholdReached.Count);
        foreach (var tally in thresholdReached)
        {
            // 零跨服务判定：仅查本域 agent_personas（AI 标记权威源），不调 User.Api（D1/D4）。
            var isAgent = await _personas.CountDocumentsAsync(
                Builders<AgentPersonaRecord>.Filter.Eq(p => p.AgentUserId, tally.TargetUserId),
                cancellationToken: cancellationToken) > 0;
            verdicts.Add(new ArenaSettlementVerdict
            {
                TargetUserId = tally.TargetUserId,
                IsAgent = isAgent,
                Verdict = isAgent ? ArenaPointsEventType.Hit : ArenaPointsEventType.Miss,
            });
        }

        var plan = new ArenaSettlementPlanRecord { WeekKey = weekKey, Verdicts = verdicts };
        try
        {
            await _plans.InsertOneAsync(plan, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // 计划已存在（prepare 幂等）：读取既有计划，不重复判定。
            var existing = await _plans.Find(Builders<ArenaSettlementPlanRecord>.Filter.Eq(p => p.WeekKey, weekKey))
                .FirstAsync(cancellationToken);
            return existing.Verdicts;
        }

        return verdicts;
    }

    /// <summary>
    /// commit：按结算计划写全部对外数据（事件 / 自白帖 / 通知 / AI 标记），全部幂等（D9 / D13）。
    /// 自白帖内容本实现用静态模板（G7 将替换为 LLM 生成 + 内容安全 P0）。
    /// </summary>
    private async Task CommitWeekAsync(string weekKey, List<ArenaSettlementVerdict> verdicts, CancellationToken cancellationToken)
    {
        var job = await _jobs.Find(Builders<ArenaRevealJobRecord>.Filter.Eq(x => x.WeekKey, weekKey))
            .FirstAsync(cancellationToken);
        var jobId = job.Id;
        var confessionPostIds = new List<string>();

        foreach (var verdict in verdicts)
        {
            var targetFilter = Builders<ArenaAccusationRecord>.Filter
                .Eq(x => x.WeekKey, weekKey) & Builders<ArenaAccusationRecord>.Filter.Eq(x => x.TargetUserId, verdict.TargetUserId);
            var accusations = await _accusations.Find(targetFilter).ToListAsync(cancellationToken);

            if (verdict.IsAgent)
            {
                // 取人设（含 Prompt / LlmProfileId）供 LLM 自白帖生成；缺失则占位（仅走静态兜底）。
                var persona = await _personas.Find(Builders<AgentPersonaRecord>.Filter
                        .Eq(p => p.AgentUserId, verdict.TargetUserId)).FirstOrDefaultAsync(cancellationToken)
                    ?? new AgentPersonaRecord { AgentUserId = verdict.TargetUserId, Nickname = verdict.TargetUserId };

                // D8 P0：LLM 路径与静态兜底路径发布前均显式 Evaluate，不使用 agent 豁免。
                var safeBody = await ResolveSafeConfessionBodyAsync(verdict.TargetUserId, persona, cancellationToken);
                if (safeBody is null)
                {
                    // 极端防御：LLM 与静态模板均被屏蔽词命中（理论上不可达）→ 保留 pending_reveal 顺延下周。
                    _logger.LogError("Arena confession for {Target} withheld: both LLM and static fallback blocked; deferred to next cycle.", verdict.TargetUserId);
                    continue;
                }

                // 生成自白帖（私有 arena_pending），完成后由门禁翻 published（D5/D7）。
                var post = new ForumPostRecord
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Title = "自白：我是一个 AI 智能体",
                    Body = safeBody,
                    AuthorSubId = verdict.TargetUserId,
                    Board = "综合",
                    Tags = new List<string> { "图灵场", "自白" },
                    State = ForumContentStates.ArenaPending,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                await _posts.InsertOneAsync(post, cancellationToken: cancellationToken);
                confessionPostIds.Add(post.Id);

                foreach (var acc in accusations)
                {
                    var eventId = ArenaEventId.Derive(weekKey, acc.AccuserId, verdict.TargetUserId, ArenaPointsEventType.Hit);
                    await AppendEventAsync(new ArenaPointsEventRecord
                    {
                        EventId = eventId,
                        UserId = acc.AccuserId,
                        Type = ArenaPointsEventType.Hit,
                        Delta = 50,
                        TargetUserId = verdict.TargetUserId,
                        AccusationId = acc.Id,
                        WeekId = weekKey,
                        NotifyEligible = true,
                    }, cancellationToken);

                    // +50 通知：uniq_eventId 幂等；BatchId 由门禁在 completed 后释放（D6/D9/D11）。
                    await WriteHitNotificationAsync(jobId, eventId, acc.AccuserId, post, cancellationToken);
                }

                // tally → pending_reveal；persona → eliminated（AI 标记权威源，D8/G8）。
                await _tallies.UpdateOneAsync(
                    Builders<ArenaTargetTallyRecord>.Filter.Eq(x => x.Id, await FindTallyId(verdict.TargetUserId, weekKey)),
                    Builders<ArenaTargetTallyRecord>.Update.Set(x => x.Status, ArenaTallyStatus.PendingReveal)
                        .Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
                    cancellationToken: cancellationToken);
                await _personas.UpdateOneAsync(
                    Builders<AgentPersonaRecord>.Filter.Eq(p => p.AgentUserId, verdict.TargetUserId),
                    Builders<AgentPersonaRecord>.Update.Set(p => p.State, AgentPersonaState.Eliminated)
                        .Set(p => p.UpdatedAtUtc, DateTime.UtcNow),
                    cancellationToken: cancellationToken);
            }
            else
            {
                // human（任意阈值）：void_human 静默取消；每位指认者 miss（-5 事件、零通知，D10）。
                foreach (var acc in accusations)
                {
                    var eventId = ArenaEventId.Derive(weekKey, acc.AccuserId, verdict.TargetUserId, ArenaPointsEventType.Miss);
                    await AppendEventAsync(new ArenaPointsEventRecord
                    {
                        EventId = eventId,
                        UserId = acc.AccuserId,
                        Type = ArenaPointsEventType.Miss,
                        Delta = -5,
                        TargetUserId = verdict.TargetUserId,
                        AccusationId = acc.Id,
                        WeekId = weekKey,
                        NotifyEligible = false,
                    }, cancellationToken);
                }

                await _tallies.UpdateOneAsync(
                    Builders<ArenaTargetTallyRecord>.Filter.Eq(x => x.Id, await FindTallyId(verdict.TargetUserId, weekKey)),
                    Builders<ArenaTargetTallyRecord>.Update.Set(x => x.Status, ArenaTallyStatus.VoidHuman)
                        .Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
                    cancellationToken: cancellationToken);
            }

            // 指认记录标记 settled（D3：结算后归档清理由归档任务调度）。
            await _accusations.UpdateManyAsync(targetFilter & Builders<ArenaAccusationRecord>.Filter.Eq(x => x.Status, ArenaAccusationStatus.Pending),
                Builders<ArenaAccusationRecord>.Update.Set(x => x.Status, ArenaAccusationStatus.Settled)
                    .Set(x => x.SettledAtUtc, DateTime.UtcNow),
                cancellationToken: cancellationToken);
        }

        // 记录本批次自白帖 Id，供门禁完成后翻 published（D5/D7）。
        await _jobs.UpdateOneAsync(
            Builders<ArenaRevealJobRecord>.Filter.Eq(x => x.WeekKey, weekKey),
            Builders<ArenaRevealJobRecord>.Update.Set(x => x.ConfessionPostIds, confessionPostIds)
                .Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 解析经内容安全校验的自白帖正文（D8 P0）。LLM 生成的 body 若被屏蔽词命中则降级为静态模板；
    /// 静态模板仍命中（理论不可达，模板由构造保证安全）则返回 null，由调用方保留 pending_reveal 顺延。
    /// </summary>
    private async Task<string?> ResolveSafeConfessionBodyAsync(string targetUserId, AgentPersonaRecord persona, CancellationToken cancellationToken)
    {
        const string title = "自白：我是一个 AI 智能体";
        var generation = await _confessionGenerator.GenerateAsync(persona, cancellationToken);
        var body = generation.Body;

        if (_blockedWords.Evaluate(title, body) != BlockedWordEvaluation.Pass)
        {
            _logger.LogWarning("Arena confession for {Target} blocked by content filter (source={Source}); falling back to static template.", targetUserId, generation.Source);
            body = ArenaConfessionTemplate.StaticBody(targetUserId);
            if (_blockedWords.Evaluate(title, body) != BlockedWordEvaluation.Pass)
                return null;
        }

        return body;
    }

    private async Task ReleaseWeekAsync(string weekKey, CancellationToken cancellationToken)
    {
        var job = await _jobs.Find(Builders<ArenaRevealJobRecord>.Filter.Eq(x => x.WeekKey, weekKey))
            .FirstAsync(cancellationToken);
        // 自白帖翻 published（D5/D7）。
        await _posts.UpdateManyAsync(
            Builders<ForumPostRecord>.Filter.In(p => p.Id, job.ConfessionPostIds) & Builders<ForumPostRecord>.Filter.Eq(p => p.State, ForumContentStates.ArenaPending),
            Builders<ForumPostRecord>.Update.Set(p => p.State, ForumContentStates.Published).Set(p => p.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);
        // 批次通知释放（D6 门禁：BatchId 存在且 ReleasedAtUtc 置位后可见）。
        await _notifications.UpdateManyAsync(
            Builders<InAppNotificationRecord>.Filter.Eq(n => n.BatchId, job.Id),
            Builders<InAppNotificationRecord>.Update.Set(n => n.ReleasedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    private async Task AppendEventAsync(ArenaPointsEventRecord ev, CancellationToken cancellationToken)
    {
        try
        {
            await _events.InsertOneAsync(ev, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // 事件 eventId 唯一（四字段），重放幂等（D9）。
        }
    }

    private async Task WriteHitNotificationAsync(string jobId, string eventId, string recipientSubId, ForumPostRecord post, CancellationToken cancellationToken)
    {
        var notif = new InAppNotificationRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            RecipientSubId = recipientSubId,
            Type = InAppNotificationTypes.ArenaRevealHit,
            EventId = eventId,
            BatchId = jobId,
            PostId = post.Id,
            PostTitle = post.Title,
            ActorSubId = post.AuthorSubId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        try
        {
            await _notifications.InsertOneAsync(notif, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // uniq_eventId 幂等（D9）：重放不重复发通知。
        }
    }

    // 反查 tally 的 _id（commit 阶段更新状态用）。prepare 已保证 (weekKey,target) 唯一。
    private async Task<string> FindTallyId(string targetUserId, string weekKey)
    {
        var tally = await _tallies.Find(Builders<ArenaTargetTallyRecord>.Filter
                .Eq(x => x.WeekKey, weekKey) & Builders<ArenaTargetTallyRecord>.Filter.Eq(x => x.TargetUserId, targetUserId))
            .FirstAsync();
        return tally.Id;
    }

    private async Task TransitionAsync(string weekKey, string state, CancellationToken cancellationToken)
    {
        await _jobs.UpdateOneAsync(
            Builders<ArenaRevealJobRecord>.Filter.Eq(x => x.WeekKey, weekKey),
            Builders<ArenaRevealJobRecord>.Update.Set(x => x.State, state).Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }
}
