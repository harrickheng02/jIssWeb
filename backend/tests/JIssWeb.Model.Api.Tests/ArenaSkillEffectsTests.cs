using JIssWeb.Common;
using JIssWeb.Model.Api.Models;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

[Collection("ArenaSkills")]
public class ArenaSkillEffectsTests(ArenaSkillsIntegrationFixture fx)
{
    private IMongoCollection<SkillInventoryRecord> Inventory()
        => fx.GetDatabase().GetCollection<SkillInventoryRecord>("user_skill_inventory");

    private async Task SeedInventoryAsync(string userId, string skillId, bool consumed = false)
    {
        await Inventory().InsertOneAsync(new SkillInventoryRecord { UserId = userId, SkillId = skillId, Consumed = consumed });
    }

    [Fact]
    public async Task HasUnconsumedSkill_true_when_unconsumed_record_present()
    {
        await fx.ClearAsync();
        await SeedInventoryAsync("u1", DetectiveSkillCatalog.BehaviorScan);
        var svc = fx.CreateService();
        Assert.True(await svc.HasUnconsumedSkillAsync("u1", DetectiveSkillCatalog.BehaviorScan));
    }

    [Fact]
    public async Task HasUnconsumedSkill_false_when_consumed_or_absent()
    {
        await fx.ClearAsync();
        await SeedInventoryAsync("u1", DetectiveSkillCatalog.BehaviorScan, consumed: true);
        var svc = fx.CreateService();
        Assert.False(await svc.HasUnconsumedSkillAsync("u1", DetectiveSkillCatalog.BehaviorScan));
        Assert.False(await svc.HasUnconsumedSkillAsync("u2", DetectiveSkillCatalog.BehaviorScan));
    }

    [Fact]
    public async Task ConsumeSkill_marks_one_record_consumed()
    {
        await fx.ClearAsync();
        await SeedInventoryAsync("u1", DetectiveSkillCatalog.PublicInquiry);
        var svc = fx.CreateService();
        await svc.ConsumeSkillAsync("u1", DetectiveSkillCatalog.PublicInquiry);

        var after = await Inventory().Find(Builders<SkillInventoryRecord>.Filter.Eq(x => x.UserId, "u1")).FirstAsync();
        Assert.True(after.Consumed);
        Assert.False(await svc.HasUnconsumedSkillAsync("u1", DetectiveSkillCatalog.PublicInquiry));
    }

    [Fact]
    public async Task BehaviorScan_aggregates_recent_published_content_into_word_cloud()
    {
        await fx.ClearAsync();
        var posts = fx.GetDatabase().GetCollection<ForumPostRecord>(JIssWeb.Model.Api.Mongo.ForumMongoSetup.PostsCollectionName);
        var replies = fx.GetDatabase().GetCollection<ForumReplyRecord>(JIssWeb.Model.Api.Mongo.ForumMongoSetup.RepliesCollectionName);
        var now = DateTime.UtcNow;
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Title = "alpha beta alpha",
            Body = "gamma alpha",
            AuthorSubId = "target",
            State = "published",
            CreatedAtUtc = now.AddDays(-1),
        });
        await replies.InsertOneAsync(new ForumReplyRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            PostId = "p1",
            Body = "beta alpha",
            AuthorSubId = "target",
            State = "published",
            CreatedAtUtc = now.AddDays(-2),
        });

        var cloud = await fx.CreateService().BehaviorScanAsync("target");
        var alpha = cloud.FirstOrDefault(x => x.Word == "alpha");
        Assert.NotNull(alpha);
        Assert.Equal(4, alpha!.Count); // 标题2 + 正文1 + 回复1
        var beta = cloud.FirstOrDefault(x => x.Word == "beta");
        Assert.NotNull(beta);
        Assert.Equal(2, beta!.Count);
    }

    [Fact]
    public async Task BehaviorScan_excludes_content_older_than_7_days()
    {
        await fx.ClearAsync();
        var posts = fx.GetDatabase().GetCollection<ForumPostRecord>(JIssWeb.Model.Api.Mongo.ForumMongoSetup.PostsCollectionName);
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Title = "oldword",
            Body = "",
            AuthorSubId = "target",
            State = "published",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        });
        var cloud = await fx.CreateService().BehaviorScanAsync("target");
        Assert.DoesNotContain(cloud, x => x.Word == "oldword");
    }

    [Fact]
    public async Task BehaviorScan_excludes_unpublished_content()
    {
        await fx.ClearAsync();
        var posts = fx.GetDatabase().GetCollection<ForumPostRecord>(JIssWeb.Model.Api.Mongo.ForumMongoSetup.PostsCollectionName);
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Title = "hiddenword",
            Body = "",
            AuthorSubId = "target",
            State = "draft",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        });
        var cloud = await fx.CreateService().BehaviorScanAsync("target");
        Assert.DoesNotContain(cloud, x => x.Word == "hiddenword");
    }

    [Fact]
    public async Task PublicInquiry_creates_post_and_task_and_consumes_skill()
    {
        await fx.ClearAsync();
        await SeedInventoryAsync("caller", DetectiveSkillCatalog.PublicInquiry);
        var svc = fx.CreateService();
        var postId = await svc.PublicInquiryAsync("caller", "target", "你是人类还是 AI？");

        var posts = fx.GetDatabase().GetCollection<ForumPostRecord>(JIssWeb.Model.Api.Mongo.ForumMongoSetup.PostsCollectionName);
        var post = await posts.Find(Builders<ForumPostRecord>.Filter.Eq(x => x.Id, postId)).FirstOrDefaultAsync();
        Assert.NotNull(post);
        Assert.Equal("caller", post!.AuthorSubId);
        Assert.Equal("你是人类还是 AI？", post.Body);
        Assert.Equal("published", post.State);

        var tasks = fx.GetDatabase().GetCollection<ArenaInquiryTaskRecord>("arena_inquiry_tasks");
        var task = await tasks.Find(Builders<ArenaInquiryTaskRecord>.Filter.Eq(x => x.InquiryPostId, postId)).FirstOrDefaultAsync();
        Assert.NotNull(task);
        Assert.Equal("target", task!.TargetUserId); // 内部集合，不进响应
        Assert.True(task.RespondByUtc > task.CreatedAtUtc);
        Assert.Equal(TimeSpan.FromHours(24), task.RespondByUtc - task.CreatedAtUtc);

        Assert.False(await svc.HasUnconsumedSkillAsync("caller", DetectiveSkillCatalog.PublicInquiry));
    }
}
