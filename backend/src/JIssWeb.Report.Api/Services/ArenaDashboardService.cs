using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JIssWeb.Report.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Report.Api.Services;

public interface IArenaDashboardService
{
    Task<ArenaDashboardViewModel> BuildAsync(CancellationToken ct = default);
}

/// <summary>
/// 运营仪表盘只读聚合服务（#36）。从 jissweb_model 库只读聚合 5 个面板，严格遵守只读纪律：
/// 本服务不得调用任何业务写方法，不得 Insert/Update/Replace 任何集合。
/// </summary>
public sealed class ArenaDashboardService : IArenaDashboardService
{
    private readonly IMongoDatabase _db;
    private readonly IMongoDatabase? _userDb;
    private readonly ArenaDashboardRevealOptions _revealOptions;
    private readonly RegistrationDashboardOptions _registrationOptions;
    private readonly IsAgentSubResolver _isAgentSub;

    public ArenaDashboardService(
        [FromKeyedServices("arena-dashboard-model")] IMongoDatabase db,
        IOptions<ArenaDashboardRevealOptions> revealOptions,
        IsAgentSubResolver isAgentSub,
        [FromKeyedServices("arena-dashboard-user")] IMongoDatabase? userDb = null,
        IOptions<RegistrationDashboardOptions>? registrationOptions = null)
    {
        _db = db;
        _userDb = userDb;
        _revealOptions = revealOptions.Value;
        _registrationOptions = registrationOptions?.Value ?? new RegistrationDashboardOptions();
        _isAgentSub = isAgentSub;
    }

    public async Task<ArenaDashboardViewModel> BuildAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var currentWeekKey = ArenaDashboardSchedule.For(nowUtc); // yyyy-MM-dd（周一）
        var startOfWeekUtc = StartOfWeekUtc(nowUtc);

        var vm = new ArenaDashboardViewModel { GeneratedAtUtc = DateTime.UtcNow };

        await Task.WhenAll(
            BuildSystemHealthAsync(vm.SystemHealth, ct),
            BuildEconomyAsync(vm.Economy, nowUtc, ct),
            BuildGameplayAsync(vm.Gameplay, nowUtc, currentWeekKey, ct),
            BuildQueueAsync(vm.Queue, ct),
            BuildCommunityAsync(vm.Community, nowUtc, startOfWeekUtc, ct),
            BuildGenerationSurvivalAsync(vm.GenerationSurvival, nowUtc, ct),
            BuildRegistrationAsync(vm.Registration, nowUtc, ct));

        return vm;
    }

    // ── 注册流入面板（只读，users 来自用户库） ──
    private async Task BuildRegistrationAsync(RegistrationPanel panel, DateTimeOffset nowUtc, CancellationToken ct)
    {
        try
        {
            panel.DailyCap = _registrationOptions.DailyCap;
            panel.RemainingToday = _registrationOptions.DailyCap > 0 ? _registrationOptions.DailyCap : -1;
            if (_userDb is null)
                return;

            var tz = ResolveTimeZone(_registrationOptions.TimeZoneId);
            var localToday = TimeZoneInfo.ConvertTime(nowUtc, tz).Date;
            var todayUtc = new DateTimeOffset(localToday, tz.GetUtcOffset(localToday)).UtcDateTime;
            var nextDayLocal = localToday.AddDays(1);
            var nextDayUtc = new DateTimeOffset(nextDayLocal, tz.GetUtcOffset(nextDayLocal)).UtcDateTime;

            var users = _userDb.GetCollection<DashboardUser>(DashboardCollectionNames.Users);
            var all = await users.Find(Builders<DashboardUser>.Filter.Empty).ToListAsync(ct);
            var today = all
                .Where(x => x.CreatedAtUtc >= todayUtc && x.CreatedAtUtc < nextDayUtc)
                .ToList();

            panel.TodayRegistered = today.Count;
            panel.TodayVerified = today.Count(x => x.EmailVerifiedAtUtc.HasValue);
            panel.PendingVerification = all.Count(x => !x.EmailVerifiedAtUtc.HasValue);

            if (_registrationOptions.DailyCap > 0)
                panel.RemainingToday = Math.Max(_registrationOptions.DailyCap - panel.TodayRegistered, 0);
        }
        catch (Exception)
        {
            // 单面板失败降级为零值，不阻断其余面板（与 GenerationSurvival 同款纪律）。
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            return TimeZoneInfo.Utc;
        }
    }

    // ── #38 世代存活曲线面板（只读） ──
    private async Task BuildGenerationSurvivalAsync(GenerationSurvivalPanel panel, DateTimeOffset nowUtc, CancellationToken ct)
    {
        try
        {
            var personas = _db.GetCollection<DashboardAgentPersona>(DashboardCollectionNames.AgentPersonas);
            var all = await personas.Find(Builders<DashboardAgentPersona>.Filter.Empty).ToListAsync(ct);
            var points = all
                .GroupBy(x => x.Generation <= 0 ? 1 : x.Generation)
                .Select(g => new GenerationSurvivalPoint
                {
                    Generation = g.Key,
                    Count = g.Count(),
                    AvgSurvivalDays = Math.Round(g.Average(x => x.SurvivalDays > 0
                        ? (double)x.SurvivalDays
                        : Math.Max(0, (nowUtc.UtcDateTime - x.CreatedAtUtc).TotalDays)), 2),
                })
                .OrderBy(x => x.Generation)
                .ToList();
            panel.Points = points;
        }
        catch (Exception)
        {
            // 单面板失败降级为空，不阻断其余面板（只读纪律不变）。
            panel.Points = new List<GenerationSurvivalPoint>();
        }
    }

    // ── 系统健康面板 ──
    private async Task BuildSystemHealthAsync(SystemHealthPanel p, CancellationToken ct)
    {
        var personas = _db.GetCollection<DashboardAgentPersona>(DashboardCollectionNames.AgentPersonas);
        var all = await personas.Find(Builders<DashboardAgentPersona>.Filter.Empty).ToListAsync(ct);
        p.AgentTotalCount = all.Count;
        p.AgentAliveCount = all.Count(x => x.State == "active");

        var sched = _db.GetCollection<DashboardSchedulerState>(DashboardCollectionNames.SchedulerStates);
        var states = await sched.Find(Builders<DashboardSchedulerState>.Filter.Empty).ToListAsync(ct);
        p.SchedulerStatus = states.Any(s => s.ConsecutiveFailures > 0 || s.LastError != null) ? "degraded" : "healthy";
        var updated = states.Where(s => s.UpdatedAtUtc.HasValue).Select(s => s.UpdatedAtUtc!.Value).ToList();
        p.SchedulerLastTickAtUtc = updated.Count != 0 ? updated.Max() : null;

        var llm = _db.GetCollection<DashboardLlmProfile>(DashboardCollectionNames.LlmProfiles);
        var profiles = await llm.Find(Builders<DashboardLlmProfile>.Filter.Empty).ToListAsync(ct);
        p.LlmProfiles = profiles
            .Select(x => new LlmProfileHealth { Name = x.Name, Enabled = x.Enabled, LastProbe = null })
            .ToList();

        var jobs = _db.GetCollection<DashboardRevealJob>(DashboardCollectionNames.RevealJobs);
        var pending = await jobs
            .Find(Builders<DashboardRevealJob>.Filter.Ne(x => x.State, "completed")).ToListAsync(ct);
        p.PendingReveal = pending
            .Select(j => new PendingRevealItem { JobId = j.Id, TargetPersonaId = "", State = j.State })
            .ToList();
    }

    // ── 经济健康面板 ──
    private async Task BuildEconomyAsync(EconomyPanel p, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var netflow = _db.GetCollection<DashboardNetflowResult>(DashboardCollectionNames.NetflowResults);
        var latestNetflow = await netflow.Find(Builders<DashboardNetflowResult>.Filter.Empty)
            .Sort(Builders<DashboardNetflowResult>.Sort.Descending(x => x.WeekKey)).Limit(1)
            .FirstOrDefaultAsync(ct);
        var weeklyNetFlow = latestNetflow?.NetFlow ?? 0;
        p.WeeklyNetFlow = weeklyNetFlow;

        var ledger = _db.GetCollection<DashboardPointsLedger>(DashboardCollectionNames.PointsLedger);
        var balances = await ledger.Find(Builders<DashboardPointsLedger>.Filter.Empty)
            .Project(Builders<DashboardPointsLedger>.Projection.Expression(x => x.Balance)).ToListAsync(ct);
        var balanceSum = balances.Sum();

        var ratio = balanceSum != 0 ? (double)weeklyNetFlow / balanceSum * 100 : 0;
        p.NetFlowRatioPct = ratio;
        if (ratio < -15) p.NetFlowLevel = "danger";
        else if (weeklyNetFlow > 0 || (latestNetflow?.InflationAlert ?? false)) p.NetFlowLevel = "warning";
        else p.NetFlowLevel = "healthy";

        var tx = _db.GetCollection<DashboardPointsTransaction>(DashboardCollectionNames.PointsTransactions);
        var startOfWeek = StartOfWeekUtc(nowUtc);
        var startOfLastWeek = startOfWeek.AddDays(-7);
        var now = nowUtc.UtcDateTime;
        var thisWeekFilter = Builders<DashboardPointsTransaction>.Filter.And(
            Builders<DashboardPointsTransaction>.Filter.Gte(x => x.OccurredAtUtc, startOfWeek),
            Builders<DashboardPointsTransaction>.Filter.Lt(x => x.OccurredAtUtc, now),
            Builders<DashboardPointsTransaction>.Filter.Gt(x => x.Delta, 0));
        var lastWeekFilter = Builders<DashboardPointsTransaction>.Filter.And(
            Builders<DashboardPointsTransaction>.Filter.Gte(x => x.OccurredAtUtc, startOfLastWeek),
            Builders<DashboardPointsTransaction>.Filter.Lt(x => x.OccurredAtUtc, startOfWeek),
            Builders<DashboardPointsTransaction>.Filter.Gt(x => x.Delta, 0));

        async Task<double> SumDeltaAsync(FilterDefinition<DashboardPointsTransaction> f)
        {
            var docs = await tx.Find(f).ToListAsync(ct);
            return docs.Sum(x => (double)x.Delta);
        }
        p.PointsInjectionDensity = await SumDeltaAsync(thisWeekFilter);
        p.LastWeekDensity = await SumDeltaAsync(lastWeekFilter);

        var aiqi = _db.GetCollection<DashboardAiqiResult>(DashboardCollectionNames.AiqiResults);
        var latestAiqi = await aiqi.Find(Builders<DashboardAiqiResult>.Filter.Empty)
            .Sort(Builders<DashboardAiqiResult>.Sort.Descending(x => x.WeekKey)).Limit(1)
            .FirstOrDefaultAsync(ct);
        p.Aiqi = latestAiqi?.Aiqi ?? 0;
        p.AiqiConfident = latestAiqi?.SampleSufficient ?? false;
    }

    // ── 游戏进展面板 ──
    private async Task BuildGameplayAsync(GameplayPanel p, DateTimeOffset nowUtc, string currentWeekKey, CancellationToken ct)
    {
        var acc = _db.GetCollection<DashboardAccusation>(DashboardCollectionNames.Accusations);
        var wkFilter = Builders<DashboardAccusation>.Filter.Eq(x => x.WeekKey, currentWeekKey);
        p.WeeklyAccusations = (int)await acc.CountDocumentsAsync(wkFilter, cancellationToken: ct);

        var top = await acc.Aggregate()
            .Match(wkFilter)
            .Group(x => x.AccuserId, g => new { AccuserId = g.Key, Count = g.Count() })
            .SortByDescending(g => g.Count)
            .Limit(3)
            .ToListAsync(ct);
        p.TopDetectives = top
            .Select(x => new TopDetectiveItem { UserId = x.AccuserId, Count = x.Count })
            .ToList();

        var bounty = _db.GetCollection<DashboardBountyState>(DashboardCollectionNames.BountyStates);
        var legendary = await bounty
            .Find(Builders<DashboardBountyState>.Filter.Eq(x => x.IsLegendaryPrey, true)).ToListAsync(ct);
        p.LegendaryPrey = legendary
            .Select(x => new LegendaryPreyItem
            {
                PersonaId = x.AgentUserId,
                SurvivedWeeks = x.SurvivedWeeks,
                CurrentBounty = x.CurrentBounty,
            })
            .ToList();

        p.NextRevealAtUtc = ArenaDashboardSchedule
            .ComputeNextRunUtc(nowUtc, _revealOptions.RevealHourLocal, _revealOptions.TimeZoneId)
            .UtcDateTime;
    }

    // ── 待处理队列面板 ──
    private async Task BuildQueueAsync(QueuePanel p, CancellationToken ct)
    {
        var reports = _db.GetCollection<DashboardForumReport>(DashboardCollectionNames.ForumReports);
        var pendingReports = await reports
            .Find(Builders<DashboardForumReport>.Filter.Eq(x => x.Status, "pending")).ToListAsync(ct);
        p.PendingReports = pendingReports
            .Select(r => new PendingReportItem { ReportId = r.Id, Reason = r.Reason })
            .ToList();

        var sched = _db.GetCollection<DashboardSchedulerState>(DashboardCollectionNames.SchedulerStates);
        var states = await sched.Find(Builders<DashboardSchedulerState>.Filter.Empty).ToListAsync(ct);
        foreach (var s in states)
        {
            if (s.ConsecutiveFailures > 0 || s.LastError != null)
                p.AgentAlerts.Add(new AgentAlertItem
                {
                    PersonaId = s.PersonaId,
                    Type = "error",
                    Detail = s.LastError,
                });
        }

        // timeout 派生：活跃 agent 超过 2 天无新帖（基于 forum_posts 最近发帖时间）。
        var personas = _db.GetCollection<DashboardAgentPersona>(DashboardCollectionNames.AgentPersonas);
        var activeAgents = await personas
            .Find(Builders<DashboardAgentPersona>.Filter.Eq(x => x.State, "active")).ToListAsync(ct);
        if (activeAgents.Count != 0)
        {
            var activeSubs = activeAgents.Select(a => a.AgentUserId).ToHashSet();
            var posts = _db.GetCollection<DashboardForumPost>(DashboardCollectionNames.ForumPosts);
            var recentByAuthor = await posts.Aggregate()
                .Group(x => x.AuthorSubId, g => new { Sub = g.Key, Last = g.Max(x => x.CreatedAtUtc) })
                .ToListAsync(ct);
            var threshold = DateTime.UtcNow.AddDays(-2);
            foreach (var agentSub in activeSubs)
            {
                var last = recentByAuthor.FirstOrDefault(r => r.Sub == agentSub)?.Last;
                if (last == null || last < threshold)
                    p.AgentAlerts.Add(new AgentAlertItem
                    {
                        PersonaId = agentSub,
                        Type = "timeout",
                        Detail = last == null ? "无发帖记录" : $"最近发帖 {last:yyyy-MM-dd HH:mm} 距今超 2 天",
                    });
            }
        }
    }

    // ── 社区数据面板（AI vs 人类判定走 join，绝不读 accountType claim） ──
    private async Task BuildCommunityAsync(CommunityPanel p, DateTimeOffset nowUtc, DateTime startOfWeekUtc, CancellationToken ct)
    {
        var posts = _db.GetCollection<DashboardForumPost>(DashboardCollectionNames.ForumPosts);
        var replies = _db.GetCollection<DashboardForumReply>(DashboardCollectionNames.ForumReplies);

        var weekPosts = await posts
            .Find(Builders<DashboardForumPost>.Filter.Gte(x => x.CreatedAtUtc, startOfWeekUtc)).ToListAsync(ct);
        p.WeeklyPostsTotal = weekPosts.Count;

        var aiPosts = 0;
        var humanPosts = 0;
        foreach (var post in weekPosts)
        {
            if (await _isAgentSub.IsAgentSubAsync(post.AuthorSubId, ct)) aiPosts++;
            else humanPosts++;
        }
        p.WeeklyPostsAi = aiPosts;
        p.WeeklyPostsHuman = humanPosts;

        var weekReplies = await replies
            .Find(Builders<DashboardForumReply>.Filter.Gte(x => x.CreatedAtUtc, startOfWeekUtc)).ToListAsync(ct);
        var subs = weekPosts.Select(x => x.AuthorSubId)
            .Concat(weekReplies.Select(x => x.AuthorSubId))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();

        var humans = 0;
        foreach (var s in subs)
        {
            if (!await _isAgentSub.IsAgentSubAsync(s, ct)) humans++;
        }
        p.WeeklyActiveHumans = humans;
    }

    private static DateTime StartOfWeekUtc(DateTimeOffset now)
    {
        var tz = ArenaDashboardSchedule.Resolve("Asia/Shanghai");
        var local = TimeZoneInfo.ConvertTime(now, tz);
        var dow = (int)local.DayOfWeek; // 周日=0
        var diff = (7 + dow - 1) % 7;
        var mondayLocal = local.Date.AddDays(-diff);
        return new DateTimeOffset(mondayLocal, tz.GetUtcOffset(mondayLocal)).UtcDateTime;
    }
}
