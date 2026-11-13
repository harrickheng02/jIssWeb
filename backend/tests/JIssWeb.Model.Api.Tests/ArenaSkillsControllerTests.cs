using System.Security.Claims;
using JIssWeb.Common;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Controllers;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Xunit;

namespace JIssWeb.Model.Api.Tests;

[Collection("ArenaSkills")]
public class ArenaSkillsControllerTests(ArenaSkillsIntegrationFixture fx)
{
    private static ClaimsPrincipal Principal(string sub)
    {
        var id = new ClaimsIdentity();
        id.AddClaim(new Claim("sub", sub));
        id.AddClaim(new Claim("userId", sub));
        return new ClaimsPrincipal(id);
    }

    private ArenaSkillsController Controller(ClaimsPrincipal user)
    {
        var ctrl = new ArenaSkillsController(fx.CreateService());
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return ctrl;
    }

    private async Task SeedInventoryAsync(string userId, string skillId)
    {
        await fx.GetDatabase().GetCollection<SkillInventoryRecord>("user_skill_inventory")
            .InsertOneAsync(new SkillInventoryRecord { UserId = userId, SkillId = skillId, Consumed = false });
    }

    [Fact]
    public async Task BehaviorScan_unowned_returns_403()
    {
        await fx.ClearAsync();
        var r = await Controller(Principal("u1")).BehaviorScan("target");
        var obj = Assert.IsType<ObjectResult>(r);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task BehaviorScan_owned_returns_word_cloud()
    {
        await fx.ClearAsync();
        await SeedInventoryAsync("u1", DetectiveSkillCatalog.BehaviorScan);
        var posts = fx.GetDatabase().GetCollection<ForumPostRecord>(JIssWeb.Model.Api.Mongo.ForumMongoSetup.PostsCollectionName);
        await posts.InsertOneAsync(new ForumPostRecord
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Title = "hello hello",
            Body = "",
            AuthorSubId = "target",
            State = "published",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        });

        var r = await Controller(Principal("u1")).BehaviorScan("target");
        var ok = Assert.IsType<OkObjectResult>(r);
        var body = Assert.IsType<ApiResult<List<WordCloudItem>>>(ok.Value);
        Assert.True(body.Success);
        Assert.Contains(body.Data!, x => x.Word == "hello" && x.Count == 2);
    }

    [Fact]
    public async Task BehaviorScan_rejects_self_target_with_bad_request()
    {
        await fx.ClearAsync();
        await SeedInventoryAsync("u1", DetectiveSkillCatalog.BehaviorScan);
        var r = await Controller(Principal("u1")).BehaviorScan("u1");
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task PublicInquiry_unowned_returns_403()
    {
        await fx.ClearAsync();
        var r = await Controller(Principal("u1")).PublicInquiry(new PublicInquiryRequest { TargetUserId = "target", Question = "?" });
        var obj = Assert.IsType<ObjectResult>(r);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task PublicInquiry_owned_creates_post_and_returns_id()
    {
        await fx.ClearAsync();
        await SeedInventoryAsync("u1", DetectiveSkillCatalog.PublicInquiry);
        var r = await Controller(Principal("u1")).PublicInquiry(new PublicInquiryRequest { TargetUserId = "target", Question = "你是人类吗？" });
        var ok = Assert.IsType<OkObjectResult>(r);
        var body = Assert.IsType<ApiResult<PublicInquiryResponse>>(ok.Value);
        Assert.True(body.Success);
        Assert.False(string.IsNullOrEmpty(body.Data!.PostId));

        var posts = fx.GetDatabase().GetCollection<ForumPostRecord>(JIssWeb.Model.Api.Mongo.ForumMongoSetup.PostsCollectionName);
        var post = await posts.Find(Builders<ForumPostRecord>.Filter.Eq(x => x.Id, body.Data.PostId)).FirstOrDefaultAsync();
        Assert.NotNull(post);
        Assert.Equal("你是人类吗？", post!.Body);
    }
}
