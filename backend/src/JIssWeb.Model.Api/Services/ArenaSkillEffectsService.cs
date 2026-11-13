using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场技能效果服务（论坛域，#35）。购买/扣费/库存登记在 Accounting.Api；本服务只负责「效果」：
/// - 行为扫描：校验调用者持有未核销 behavior_scan，聚合目标近 7 日帖/回复成词云。
/// - 公开质询：校验调用者持有未核销 public_inquiry，生成公开帖 + 24h 回应任务（agent 调度感知）+ 核销技能。
/// 通过共享 Mongo 库直接读取 user_skill_inventory（本地常量名，避免反向依赖 Accounting.Api）。
/// </summary>
public sealed class ArenaSkillEffectsService
{
    // 与 Accounting.Api.Mongo.AccountingMongoSetup.SkillInventoryCollectionName 保持一致（同库同集合）。
    private const string InventoryCollection = "user_skill_inventory";
    private const string InquiryTasksCollection = "arena_inquiry_tasks";
    private static readonly TimeSpan InquiryWindow = TimeSpan.FromHours(24);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "on", "for", "is", "are", "was", "were",
        "this", "that", "it", "as", "at", "by", "be", "with", "from", "we", "you", "i", "he", "she",
    };

    private readonly IMongoCollection<SkillInventoryRecord> _inventory;
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumReplyRecord> _replies;
    private readonly IMongoCollection<ArenaInquiryTaskRecord> _inquiryTasks;

    public ArenaSkillEffectsService(IMongoDatabase db)
    {
        _inventory = db.GetCollection<SkillInventoryRecord>(InventoryCollection);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _replies = db.GetCollection<ForumReplyRecord>(ForumMongoSetup.RepliesCollectionName);
        _inquiryTasks = db.GetCollection<ArenaInquiryTaskRecord>(InquiryTasksCollection);
    }

    /// <summary>调用者是否持有指定技能且未核销。</summary>
    public async Task<bool> HasUnconsumedSkillAsync(string userId, string skillId, CancellationToken ct = default)
    {
        var f = Builders<SkillInventoryRecord>.Filter;
        return await _inventory.Find(
                f.Eq(x => x.UserId, userId) & f.Eq(x => x.SkillId, skillId) & f.Eq(x => x.Consumed, false))
            .AnyAsync(ct);
    }

    /// <summary>核销调用者的一条指定技能记录（一次性消耗品）。</summary>
    public async Task ConsumeSkillAsync(string userId, string skillId, CancellationToken ct = default)
    {
        var f = Builders<SkillInventoryRecord>.Filter;
        await _inventory.UpdateManyAsync(
            f.Eq(x => x.UserId, userId) & f.Eq(x => x.SkillId, skillId) & f.Eq(x => x.Consumed, false),
            Builders<SkillInventoryRecord>.Update.Set(x => x.Consumed, true),
            cancellationToken: ct);
    }

    /// <summary>
    /// 行为扫描：聚合目标近 7 日已发布帖/回复文本成词云（Top 20）。
    /// 不含 targetUserId / eventId（隐私红线）。
    /// </summary>
    public async Task<List<WordCloudItem>> BehaviorScanAsync(
        string targetUserId, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var postF = Builders<ForumPostRecord>.Filter;
        var posts = await _posts.Find(
                postF.Eq(x => x.AuthorSubId, targetUserId)
                & postF.Eq(x => x.State, ForumContentStates.Published)
                & postF.Gte(x => x.CreatedAtUtc, since))
            .ToListAsync(ct);

        var replyF = Builders<ForumReplyRecord>.Filter;
        var replies = await _replies.Find(
                replyF.Eq(x => x.AuthorSubId, targetUserId)
                & replyF.Eq(x => x.State, ForumContentStates.Published)
                & replyF.Gte(x => x.CreatedAtUtc, since))
            .ToListAsync(ct);

        var corpus = string.Join(" ",
            posts.Select(p => p.Title + " " + p.Body).Concat(replies.Select(r => r.Body)));
        return BuildWordCloud(corpus);
    }

    /// <summary>
    /// 公开质询：生成公开帖（作者=调用者，状态 published）+ 记录 24h 回应任务 + 核销技能。
    /// 目标关联仅存于内部 arena_inquiry_tasks（TargetUserId 不进帖子正文/API 响应）。
    /// </summary>
    public async Task<string> PublicInquiryAsync(
        string callerUserId, string targetUserId, string question, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var post = new ForumPostRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Title = "公开质询",
            Body = question,
            AuthorSubId = callerUserId,
            Board = "综合",
            Tags = new List<string>(),
            CreatedAtUtc = now,
            State = ForumContentStates.Published,
        };
        await _posts.InsertOneAsync(post, cancellationToken: ct);

        await _inquiryTasks.InsertOneAsync(new ArenaInquiryTaskRecord
        {
            TargetUserId = targetUserId,
            InquiryPostId = post.Id,
            CreatedAtUtc = now,
            RespondByUtc = now.Add(InquiryWindow),
            Responded = false,
        }, cancellationToken: ct);

        await ConsumeSkillAsync(callerUserId, DetectiveSkillCatalog.PublicInquiry, ct);
        return post.Id;
    }

    private static List<WordCloudItem> BuildWordCloud(string corpus)
    {
        if (string.IsNullOrWhiteSpace(corpus))
            return new List<WordCloudItem>();

        var lower = corpus.ToLowerInvariant();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        while (i < lower.Length)
        {
            var c = lower[i];
            if (char.IsLetterOrDigit(c))
            {
                // ASCII 字母/数字：连续抽取成词；CJK 字符：单字成词。
                if (c > 127)
                {
                    var ch = lower[i].ToString();
                    if (!StopWords.Contains(ch))
                        Increment(counts, ch);
                    i++;
                }
                else
                {
                    var j = i;
                    while (j < lower.Length && char.IsLetterOrDigit(lower[j]) && lower[j] <= 127)
                        j++;
                    var token = lower.Substring(i, j - i);
                    if (token.Length >= 2 && !StopWords.Contains(token))
                        Increment(counts, token);
                    i = j;
                }
            }
            else
            {
                i++;
            }
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(20)
            .Select(kv => new WordCloudItem(kv.Key, kv.Value))
            .ToList();
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out var v);
        counts[key] = v + 1;
    }
}
