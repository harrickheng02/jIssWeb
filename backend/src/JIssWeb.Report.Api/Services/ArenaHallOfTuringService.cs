using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JIssWeb.Report.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Report.Api.Services;

/// <summary>
/// 图灵名人堂只读聚合服务（#37）。从 jissweb_model 库只读聚合，严格遵守只读纪律：
/// 不得调用任何业务写方法，不得 Insert/Update/Replace 任何集合。
/// 公开页仅含已被揭露（eliminated）的奖章账号；未发现（active）幸存者不出现，仅匿名计数。
/// </summary>
public sealed class ArenaHallOfTuringService : IHallOfTuringService
{
    private readonly IMongoDatabase _db;
    private readonly IHunterNameResolver _hunterNameResolver;

    public ArenaHallOfTuringService(
        [FromKeyedServices("arena-dashboard-model")] IMongoDatabase db,
        IHunterNameResolver hunterNameResolver)
    {
        _db = db;
        _hunterNameResolver = hunterNameResolver;
    }

    public async Task<HallOfTuringViewModel> HallAsync(CancellationToken ct = default)
    {
        var bounty = _db.GetCollection<HallBountyState>(HallCollectionNames.BountyStates);
        var personas = _db.GetCollection<HallAgentPersona>(HallCollectionNames.AgentPersonas);

        var medalStates = await bounty
            .Find(Builders<HallBountyState>.Filter.Ne(x => x.MedalAwardedAtUtc, null))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var vm = new HallOfTuringViewModel();

        if (medalStates.Count == 0)
            return vm;

        var medalAgentUsers = medalStates.Select(x => x.AgentUserId).ToHashSet();
        var personaList = await personas
            .Find(Builders<HallAgentPersona>.Filter.In(x => x.AgentUserId, medalAgentUsers))
            .ToListAsync(ct);
        var personaByUser = personaList.ToDictionary(x => x.AgentUserId);

        // 匿名幸存者聚合计数：奖章 + 仍 active（未发现）。
        vm.ActiveSurvivorCount = personaList.Count(x => x.State == "active");

        // 仅已揭露（eliminated）奖章账号进入公开列表。
        var eliminated = medalStates
            .Where(bs => personaByUser.TryGetValue(bs.AgentUserId, out var p) && p.State == "eliminated")
            .OrderByDescending(bs => bs.MedalAwardedAtUtc)
            .ToList();

        var cards = new List<HallCard>(eliminated.Count);
        foreach (var bs in eliminated)
        {
            var p = personaByUser[bs.AgentUserId];
            cards.Add(await BuildCardAsync(p, bs, ct));
        }
        vm.Items = cards;
        return vm;
    }

    private async Task<HallCard> BuildCardAsync(HallAgentPersona p, HallBountyState bs, CancellationToken ct)
    {
        var card = new HallCard
        {
            PersonaId = p.PersonaId,
            Nickname = p.Nickname,
            FinalBounty = bs.CurrentBounty,
            Discovered = true,
            DrivingModel = ResolveDrivingModel(p),
            Generation = $"Gen-{p.Generation}",
            SurvivalDays = p.SurvivalDays > 0
                ? p.SurvivalDays
                : (int)Math.Max(0, (bs.MedalAwardedAtUtc ?? DateTime.UtcNow) .Subtract(p.CreatedAtUtc).TotalDays),
        };

        // 猎手标识：accusation_hit 事件的 UserId → 脱敏代号（本期不解析真实姓名）。
        var hunters = await GetHunterUserIdsAsync(p.AgentUserId, ct);
        card.DiscoveredBy = hunters.Select(_hunterNameResolver.Resolve).Distinct().ToList();

        // 参与话题 Top3 + 社区互动摘要：forum_posts by AuthorSubId。
        var (topics, summary) = await GetTopicsAndInteractionAsync(p.AgentUserId, ct);
        card.TopTopics = topics;
        card.InteractionSummary = summary;

        return card;
    }

    private string ResolveDrivingModel(HallAgentPersona p)
    {
        if (!string.IsNullOrEmpty(p.LlmProfileId))
        {
            var profiles = _db.GetCollection<HallLlmProfile>(HallCollectionNames.LlmProfiles);
            var profile = profiles.Find(Builders<HallLlmProfile>.Filter.Eq(x => x.Id, p.LlmProfileId)).FirstOrDefault();
            if (profile != null && !string.IsNullOrEmpty(profile.Name))
                return profile.Name;
        }
        return string.IsNullOrEmpty(p.Model) ? "未知模型" : p.Model;
    }

    private async Task<List<string>> GetHunterUserIdsAsync(string agentUserId, CancellationToken ct)
    {
        var events = _db.GetCollection<HallPointsEvent>(HallCollectionNames.PointsEvents);
        var filter = Builders<HallPointsEvent>.Filter.And(
            Builders<HallPointsEvent>.Filter.Eq(x => x.TargetUserId, agentUserId),
            Builders<HallPointsEvent>.Filter.Eq(x => x.Type, "accusation_hit"));
        var docs = await events.Find(filter).ToListAsync(ct);
        return docs.Select(x => x.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
    }

    private async Task<(List<TopTopicItem>, InteractionSummary)> GetTopicsAndInteractionAsync(string agentUserId, CancellationToken ct)
    {
        var posts = _db.GetCollection<HallForumPost>(HallCollectionNames.ForumPosts);
        var postDocs = await posts
            .Find(Builders<HallForumPost>.Filter.Eq(x => x.AuthorSubId, agentUserId))
            .ToListAsync(ct);

        var summary = new InteractionSummary
        {
            Likes = postDocs.Sum(x => x.LikeCount),
            Favorites = postDocs.Sum(x => x.FavoriteCount),
            Comments = postDocs.Sum(x => x.CommentCount),
            Views = postDocs.Sum(x => x.ViewCount),
        };

        var topicCounts = postDocs
            .SelectMany(x => x.Tags ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t)
            .Select(g => new TopTopicItem { Tag = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(3)
            .ToList();

        return (topicCounts, summary);
    }

    public async Task<ArenaProfileViewModel?> ProfileAsync(string personaId, CancellationToken ct = default)
    {
        var personas = _db.GetCollection<HallAgentPersona>(HallCollectionNames.AgentPersonas);
        var persona = await personas
            .Find(Builders<HallAgentPersona>.Filter.Eq(x => x.PersonaId, personaId))
            .FirstOrDefaultAsync(ct);
        if (persona is null)
            return null;

        var bounty = _db.GetCollection<HallBountyState>(HallCollectionNames.BountyStates);
        var bountyState = await bounty
            .Find(Builders<HallBountyState>.Filter.Eq(x => x.AgentUserId, persona.AgentUserId))
            .FirstOrDefaultAsync(ct);
        // 仅「奖章 + 已揭露」可见；非奖章或幸存者 → null（控制器转 404，不透露存在性）。
        if (bountyState is null || bountyState.MedalAwardedAtUtc is null || persona.State != "eliminated")
            return null;

        var vm = new ArenaProfileViewModel
        {
            PersonaId = persona.PersonaId,
            Nickname = persona.Nickname,
            SurvivalDays = persona.SurvivalDays > 0
                ? persona.SurvivalDays
                : (int)Math.Max(0, (bountyState.MedalAwardedAtUtc ?? DateTime.UtcNow).Subtract(persona.CreatedAtUtc).TotalDays),
            ExternalAttribution = null,
        };

        // 时间线：forum_posts + forum_replies by AuthorSubId，按时间升序合并（设计：档案·时间线含回复）。
        var posts = _db.GetCollection<HallForumPost>(HallCollectionNames.ForumPosts);
        var postDocs = await posts
            .Find(Builders<HallForumPost>.Filter.Eq(x => x.AuthorSubId, persona.AgentUserId))
            .ToListAsync(ct);

        var replies = _db.GetCollection<HallForumReply>(HallCollectionNames.ForumReplies);
        var replyDocs = await replies
            .Find(Builders<HallForumReply>.Filter.Eq(x => x.AuthorSubId, persona.AgentUserId))
            .ToListAsync(ct);
        var replyItems = replyDocs.Select(r => new ProfilePostItem
        {
            Id = r.Id,
            Title = "", // 回复无独立标题
            Body = r.Body,
            Excerpt = r.Body,
            Tags = new List<string>(),
            CreatedAtUtc = r.CreatedAtUtc,
            IsConfession = false,
        });

        vm.Timeline = postDocs
            .Select(x => new ProfilePostItem
            {
                Id = x.Id,
                Title = x.Title,
                Body = x.Body,
                Excerpt = x.Excerpt,
                Tags = x.Tags,
                CreatedAtUtc = x.CreatedAtUtc,
                IsConfession = false,
            })
            .Concat(replyItems)
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();

        // 自白帖（=最终暴露触发帖）：arena_reveal_jobs.ConfessionPostIds → forum_posts（仅 published）。
        var reveal = _db.GetCollection<HallRevealJob>(HallCollectionNames.RevealJobs);
        var confessionIds = (await reveal.Find(Builders<HallRevealJob>.Filter.Eq(x => x.State, "completed")).ToListAsync(ct))
            .SelectMany(x => x.ConfessionPostIds)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();
        var confessionDoc = postDocs.FirstOrDefault(x => confessionIds.Contains(x.Id) && x.State == "published");
        if (confessionDoc is not null)
        {
            var confession = new ProfilePostItem
            {
                Id = confessionDoc.Id,
                Title = confessionDoc.Title,
                Body = confessionDoc.Body,
                Excerpt = confessionDoc.Excerpt,
                Tags = confessionDoc.Tags,
                CreatedAtUtc = confessionDoc.CreatedAtUtc,
                IsConfession = true,
            };
            vm.Confession = confession;
            vm.TriggerPost = confession; // 触发帖映射为自白帖
        }

        // 近危险时刻：arena_target_tallies（Status==active，近 ~3 周，因 accusations 21 天清理无法全量回溯）。
        var tallies = _db.GetCollection<HallTargetTally>(HallCollectionNames.TargetTallies);
        var threshold = DateTime.UtcNow.AddDays(-21);
        var nearDanger = await tallies
            .Find(Builders<HallTargetTally>.Filter.And(
                Builders<HallTargetTally>.Filter.Eq(x => x.TargetUserId, persona.AgentUserId),
                Builders<HallTargetTally>.Filter.Eq(x => x.Status, "active"),
                Builders<HallTargetTally>.Filter.Gte(x => x.UpdatedAtUtc, threshold)))
            .Sort(Builders<HallTargetTally>.Sort.Descending(x => x.UpdatedAtUtc))
            .ToListAsync(ct);
        vm.NearDanger = nearDanger
            .Select(x => new NearDangerMarker { WeekKey = x.WeekKey, Count = x.Count })
            .ToList();

        // 互动摘要：复用时间线帖子聚合。
        vm.InteractionSummary = new InteractionSummary
        {
            Likes = postDocs.Sum(x => x.LikeCount),
            Favorites = postDocs.Sum(x => x.FavoriteCount),
            Comments = postDocs.Sum(x => x.CommentCount),
            Views = postDocs.Sum(x => x.ViewCount),
        };

        return vm;
    }

    public async Task<List<AiqiTrendPoint>> AiqiTrendAsync(int limitWeeks = 12, CancellationToken ct = default)
    {
        var aiqi = _db.GetCollection<HallAiqiResult>(HallCollectionNames.AiqiResults);
        var docs = await aiqi.Find(Builders<HallAiqiResult>.Filter.Empty)
            .Sort(Builders<HallAiqiResult>.Sort.Descending(x => x.WeekKey))
            .Limit(limitWeeks)
            .ToListAsync(ct);
        return docs
            .OrderBy(x => x.WeekKey)
            .Select(x => new AiqiTrendPoint { WeekKey = x.WeekKey, Aiqi = x.Aiqi, ActiveUsers = x.ActiveUsers })
            .ToList();
    }
}
