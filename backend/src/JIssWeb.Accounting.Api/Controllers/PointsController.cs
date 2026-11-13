using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Models;
using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace JIssWeb.Accounting.Api.Controllers;

[ApiController]
[Route("api/accounting/points")]
[Authorize]
public class PointsController : ControllerBase
{
    private readonly IMongoCollection<UserPointsLedgerRecord> _ledger;
    private readonly IMongoCollection<UserPointsTransactionRecord> _transactions;

    public PointsController(IMongoDatabase db)
    {
        _ledger = db.GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        _transactions = db.GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
    }

    /// <summary>
    /// 余额查询。仅本人可查；版主/管理员可查他人（?userId=）。响应仅含余额，零隐私字段。
    /// </summary>
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance([FromQuery] string? userId)
    {
        var caller = User.GetUserId();
        var target = ResolveTarget(caller, userId);
        if (target is null) return Forbid();

        var ledger = await _ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, target))
            .FirstOrDefaultAsync();
        return Ok(ApiResult<PointsBalanceResponse>.Ok(new PointsBalanceResponse(ledger?.Balance ?? 0)));
    }

    /// <summary>
    /// 历史明细。仅本人或版主/管理员。响应不含 eventId / targetUserId。
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? userId, [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var caller = User.GetUserId();
        var target = ResolveTarget(caller, userId);
        if (target is null) return Forbid();

        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 100);
        var items = await _transactions
            .Find(Builders<UserPointsTransactionRecord>.Filter.Eq(x => x.UserId, target))
            .Sort(Builders<UserPointsTransactionRecord>.Sort.Descending(x => x.OccurredAtUtc))
            .Skip((page - 1) * size).Limit(size).ToListAsync();

        var dtos = items.Select(i => new PointsHistoryItem(i.Type, i.Delta, i.BalanceAfter, i.OccurredAtUtc)).ToList();
        return Ok(ApiResult<List<PointsHistoryItem>>.Ok(dtos));
    }

    /// <summary>
    /// 解析目标用户：未传 userId 取本人；传他人需版主/管理员权限。
    /// </summary>
    private string? ResolveTarget(string caller, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return caller;
        if (requested == caller) return caller;
        var role = User.GetForumPrincipalRole();
        return role is ForumPrincipalRole.Moderator or ForumPrincipalRole.Admin ? requested : null;
    }
}

public record PointsBalanceResponse(int Balance);

/// <summary>
/// 历史明细项：仅类型/金额/发生后余额/时间。不含 eventId / targetUserId（隐私红线）。
/// </summary>
public record PointsHistoryItem(string Type, int Delta, int BalanceAfter, DateTime OccurredAtUtc);
