using JIssWeb.Accounting.Api.Controllers;
using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Models;
using JIssWeb.Accounting.Api.Services;
using JIssWeb.Common;
using JIssWeb.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Security.Claims;
using Xunit;

namespace JIssWeb.Accounting.Api.Tests;

[Collection("Accounting")]
public class ArenaPointsApiTests(AccountingIntegrationFixture fx)
{
    private static ClaimsPrincipal Principal(string sub, string? role = null)
    {
        var id = new ClaimsIdentity();
        id.AddClaim(new Claim("sub", sub));
        id.AddClaim(new Claim("userId", sub));
        if (role is not null) id.AddClaim(new Claim(ForumRoleClaim.Name, role));
        return new ClaimsPrincipal(id);
    }

    private static T WithUser<T>(T controller, ClaimsPrincipal user) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return controller;
    }

    private async Task SeedLedgerAsync(string userId, int balance)
    {
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        await ledger.InsertOneAsync(new UserPointsLedgerRecord { UserId = userId, Balance = balance });
    }

    private async Task<int> BalanceOfAsync(string userId)
    {
        var ledger = fx.GetDatabase().GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        var doc = await ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, userId)).FirstOrDefaultAsync();
        return doc?.Balance ?? 0;
    }

    [Fact]
    public async Task Balance_returns_own_balance()
    {
        await fx.ClearAsync();
        await SeedLedgerAsync("self", 120);
        var ctrl = WithUser(new PointsController(fx.GetDatabase()), Principal("self"));

        var r = await ctrl.GetBalance(null);
        var ok = Assert.IsType<OkObjectResult>(r);
        var body = Assert.IsType<ApiResult<PointsBalanceResponse>>(ok.Value);
        Assert.True(body.Success);
        Assert.Equal(120, body.Data!.Balance);
    }

    [Fact]
    public async Task Balance_querying_other_as_member_is_forbidden()
    {
        await fx.ClearAsync();
        await SeedLedgerAsync("other", 50);
        var ctrl = WithUser(new PointsController(fx.GetDatabase()), Principal("self"));

        var r = await ctrl.GetBalance("other");
        Assert.IsType<ForbidResult>(r);
    }

    [Fact]
    public async Task Balance_querying_other_as_moderator_succeeds()
    {
        await fx.ClearAsync();
        await SeedLedgerAsync("other", 50);
        var ctrl = WithUser(new PointsController(fx.GetDatabase()), Principal("self", ForumRoleClaim.Moderator));

        var r = await ctrl.GetBalance("other");
        var ok = Assert.IsType<OkObjectResult>(r);
        var body = Assert.IsType<ApiResult<PointsBalanceResponse>>(ok.Value);
        Assert.Equal(50, body.Data!.Balance);
    }

    [Fact]
    public async Task History_response_has_no_privacy_leak_fields()
    {
        await fx.ClearAsync();
        await SeedLedgerAsync("self", 100);
        var tx = fx.GetDatabase().GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        await tx.InsertOneAsync(new UserPointsTransactionRecord
        {
            TxId = "t1", UserId = "self", Type = "accusation_hit", Delta = 50, BalanceAfter = 100, OccurredAtUtc = DateTime.UtcNow,
        });
        var ctrl = WithUser(new PointsController(fx.GetDatabase()), Principal("self"));

        var r = await ctrl.GetHistory(null);
        var ok = Assert.IsType<OkObjectResult>(r);
        var body = Assert.IsType<ApiResult<List<PointsHistoryItem>>>(ok.Value);
        Assert.Single(body.Data!);
        var item = body.Data![0];
        // 序列化后不应出现 eventId / targetUserId。
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        Assert.DoesNotContain("eventId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("targetUserId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Purchase_valid_skill_deducts_and_grants()
    {
        await fx.ClearAsync();
        await SeedLedgerAsync("self", 100);
        var svc = new ArenaSkillService(fx.GetDatabase());
        var ctrl = WithUser(new SkillsController(svc), Principal("self"));

        var r = await ctrl.Purchase(new SkillPurchaseRequest(DetectiveSkillCatalog.BehaviorScan));
        var ok = Assert.IsType<OkObjectResult>(r);
        var body = Assert.IsType<ApiResult<object>>(ok.Value);
        Assert.True(body.Success);

        Assert.Equal(70, await BalanceOfAsync("self")); // 100 - 30
        var inv = fx.GetDatabase().GetCollection<UserSkillInventoryRecord>(AccountingMongoSetup.SkillInventoryCollectionName);
        var rows = await inv.Find(Builders<UserSkillInventoryRecord>.Filter.Eq(x => x.UserId, "self")).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Purchase_insufficient_balance_is_rejected()
    {
        await fx.ClearAsync();
        await SeedLedgerAsync("self", 10);
        var svc = new ArenaSkillService(fx.GetDatabase());
        var ctrl = WithUser(new SkillsController(svc), Principal("self"));

        var r = await ctrl.Purchase(new SkillPurchaseRequest(DetectiveSkillCatalog.PublicInquiry)); // 80 > 10
        var ok = Assert.IsType<OkObjectResult>(r);
        var body = Assert.IsType<ApiResult<object>>(ok.Value);
        Assert.False(body.Success);
        Assert.Equal(10, await BalanceOfAsync("self")); // 未扣费
    }

    [Fact]
    public async Task Purchase_invalid_skill_is_rejected()
    {
        await fx.ClearAsync();
        await SeedLedgerAsync("self", 100);
        var svc = new ArenaSkillService(fx.GetDatabase());
        var ctrl = WithUser(new SkillsController(svc), Principal("self"));

        var r = await ctrl.Purchase(new SkillPurchaseRequest("bogus"));
        var ok = Assert.IsType<OkObjectResult>(r);
        var body = Assert.IsType<ApiResult<object>>(ok.Value);
        Assert.False(body.Success);
    }
}
